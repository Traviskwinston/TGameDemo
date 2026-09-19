using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Two separate behaviours, deliberately gated so cover never grabs you by accident:
    ///
    ///  - Mid cover (crates, low walls): only attaches while crouched.
    ///  - Tall walls: only attach when a corner is within reach ahead, so walking a long
    ///    corridor never sticks you to the wall. Reaching the edge lets you lean around it.
    ///
    /// Attachment always needs the stick pushed roughly at the surface, and releasing is
    /// just pulling away from it.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class TlouCover : MonoBehaviour
    {
        public enum CoverMode
        {
            None,
            Low,
            WallCorner
        }

        [Header("Refs")]
        public TlouPlayerMotor motor;
        public TlouCamera cam;
        public LayerMask coverMask = ~0;

        [Header("Detection")]
        [Tooltip("How far ahead we look for a cover face.")]
        public float probeDistance = 0.85f;
        [Tooltip("Gap kept between body and cover face.")]
        public float stickGap = 0.34f;
        [Tooltip("Stick must be pushed at least this much at the face to attach.")]
        public float minPressIn = 0.4f;
        public float detachCooldown = 0.35f;

        [Header("Wall Corners")]
        [Tooltip("A tall wall only becomes cover when a corner is this close along the face.")]
        public float cornerDetectDistance = 1.9f;
        public float cornerProbeStep = 0.18f;
        [Tooltip("Require movement toward the corner unless we are already this close to it.")]
        public float cornerAutoRange = 0.8f;
        public bool requireCrouchForWalls = false;

        [Header("Peek")]
        [Tooltip("Stop sliding this far short of the edge so the body stays hidden.")]
        public float edgePadding = 0.26f;
        public float peekOutDistance = 0.42f;
        public float peekLeanSpeed = 8f;
        [Tooltip("Pushing past a corner without aiming leans for this long, then lets go and keeps walking.")]
        public float edgeReleaseDelay = 0.16f;

        [Header("Feel")]
        [Tooltip("How quickly the body settles onto the face. Lower is softer.")]
        public float attachSmoothing = 0.11f;
        [Tooltip("Body turn rate onto the cover facing.")]
        public float faceTurnSpeed = 9f;

        /// <summary>True while a hand should rest on the wall. Only for tall walls.</summary>
        public bool HasWallHand { get; private set; }
        /// <summary>World point on the wall where the near hand rests.</summary>
        public Vector3 WallHandPoint { get; private set; }
        /// <summary>Wall surface normal at that point, facing the player.</summary>
        public Vector3 WallHandNormal { get; private set; }
        /// <summary>+1 right hand on the wall, -1 left.</summary>
        public int WallHandSide { get; private set; }

        public bool IsInCover => Mode != CoverMode.None;
        public CoverMode Mode { get; private set; } = CoverMode.None;
        public CoverSurface Current { get; private set; }

        /// <summary>-1 leaning left past the edge, +1 right, 0 tucked in.</summary>
        public int PeekSide { get; private set; }
        public bool PeekOver { get; private set; }

        /// <summary>0..1 how far the lean has blended out. Drives camera and avatar.</summary>
        public float PeekBlend { get; private set; }

        /// <summary>Outward from the cover face, into the open.</summary>
        public Vector3 FaceNormal => _faceNormal;

        /// <summary>Along the face, outward-facing right.</summary>
        public Vector3 FaceTangent => _faceTangent;

        /// <summary>Which way the usable corner lies, as a tangent sign. 0 when none.</summary>
        public int CornerSide { get; private set; }

        Vector3 _faceNormal;
        Vector3 _faceTangent;
        float _along;
        float _limitPos;
        float _limitNeg;
        float _cooldown;
        float _peekTarget;
        float _edgeHold;
        Vector3 _attachVel;

        void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<TlouPlayerMotor>();
            }

            if (cam == null)
            {
                cam = FindFirstObjectByType<TlouCamera>();
            }
        }

        void Update()
        {
            if (motor == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (_cooldown > 0f)
            {
                _cooldown -= dt;
            }

            if (motor.MovementLocked)
            {
                if (IsInCover)
                {
                    Release();
                }

                return;
            }

            if (Mode == CoverMode.None)
            {
                TryAttach();
            }
            else
            {
                Maintain(dt);
            }

            PeekBlend = Mathf.MoveTowards(PeekBlend, _peekTarget, peekLeanSpeed * dt);
        }

        // ---------------------------------------------------------------- attach

        void TryAttach()
        {
            if (_cooldown > 0f || motor.Stance == PlayerStance.Prone || motor.IsSprinting)
            {
                return;
            }

            Vector2 move = motor.MoveInput;
            if (move.sqrMagnitude < 0.04f)
            {
                return;
            }

            Vector3 moveDir = Quaternion.Euler(0f, motor.CameraYaw, 0f) * new Vector3(move.x, 0f, move.y);
            moveDir.y = 0f;
            moveDir.Normalize();

            float chest = Mathf.Max(0.45f, motor.Controller.height * 0.55f);
            Vector3 origin = transform.position + Vector3.up * chest;
            Vector3 side = Vector3.Cross(Vector3.up, moveDir).normalized;

            // Forward catches walking into cover. The two side rays catch walls you are
            // travelling alongside, which is the only way a corner hug can ever trigger.
            if (TryFace(origin, moveDir, moveDir, requirePressIn: true))
            {
                return;
            }

            if (TryFace(origin, side, moveDir, requirePressIn: false))
            {
                return;
            }

            TryFace(origin, -side, moveDir, requirePressIn: false);
        }

        bool TryFace(Vector3 origin, Vector3 rayDir, Vector3 moveDir, bool requirePressIn)
        {
            if (!Physics.Raycast(origin, rayDir, out RaycastHit hit, probeDistance, coverMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            CoverSurface surf = hit.collider.GetComponentInParent<CoverSurface>();
            if (surf == null || surf.Col == null)
            {
                return false;
            }

            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude < 0.01f)
            {
                return false;
            }

            n.Normalize();

            if (surf.kind == CoverSurface.CoverKind.Mid)
            {
                // Low cover is always deliberate: crouched, and pushed at the face.
                if (motor.Stance != PlayerStance.Crouch)
                {
                    return false;
                }

                if (Vector3.Dot(moveDir, -n) < minPressIn)
                {
                    return false;
                }

                Enter(CoverMode.Low, surf, n, hit.point, 0);
                return true;
            }

            if (requireCrouchForWalls && motor.Stance != PlayerStance.Crouch)
            {
                return false;
            }

            // A wall you walk straight into still needs the push; one you pass needs only a corner.
            if (requirePressIn && Vector3.Dot(moveDir, -n) < minPressIn)
            {
                return false;
            }

            int cornerSide = FindCorner(surf, n, hit.point, moveDir, out float cornerDist);
            if (cornerSide == 0 || cornerDist > cornerDetectDistance)
            {
                return false;
            }

            Enter(CoverMode.WallCorner, surf, n, hit.point, cornerSide);
            return true;
        }

        /// <summary>
        /// Walks outward along the face in both directions until the wall stops existing.
        /// Returns the tangent sign of the nearest usable corner, or 0 if there is none close.
        /// </summary>
        int FindCorner(CoverSurface surf, Vector3 faceNormal, Vector3 facePoint, Vector3 moveDir,
            out float bestDistance)
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, faceNormal).normalized;
            float y = facePoint.y;
            bestDistance = float.MaxValue;
            int bestSide = 0;

            for (int s = -1; s <= 1; s += 2)
            {
                for (float d = cornerProbeStep; d <= cornerDetectDistance + 0.01f; d += cornerProbeStep)
                {
                    Vector3 probe = facePoint + tangent * (s * d) + faceNormal * 0.3f;
                    probe.y = y;

                    bool stillWall = Physics.Raycast(probe, -faceNormal, out RaycastHit h, 0.6f, coverMask,
                        QueryTriggerInteraction.Ignore);

                    if (stillWall && h.collider.GetComponentInParent<CoverSurface>() == surf)
                    {
                        continue;
                    }

                    // Wall ended here: that is the corner on this side.
                    bool approaching = Vector3.Dot(moveDir, tangent * s) > 0.15f;
                    if (!approaching && d > cornerAutoRange)
                    {
                        break;
                    }

                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestSide = s;
                    }

                    break;
                }
            }

            return bestSide;
        }

        void Enter(CoverMode mode, CoverSurface surf, Vector3 normal, Vector3 hitPoint, int cornerSide)
        {
            Mode = mode;
            Current = surf;
            CornerSide = cornerSide;
            _faceNormal = normal;
            _faceTangent = Vector3.Cross(Vector3.up, normal).normalized;
            PeekSide = 0;
            PeekOver = false;
            _peekTarget = 0f;
            _edgeHold = 0f;

            // Carry the approach velocity into the glide so the settle is continuous.
            _attachVel = motor.Velocity;
            _attachVel.y = 0f;

            Vector3 facePoint = surf.FacePlanePoint(normal, transform.position.y);
            _along = Vector3.Dot(hitPoint - facePoint, _faceTangent);

            MeasureFaceLimits(surf, facePoint);

            motor.InCover = true;
            if (mode == CoverMode.Low)
            {
                motor.TrySetStance(PlayerStance.Crouch);
            }

            SnapToFace(0f, Mathf.Max(Time.deltaTime, 0.001f));
        }

        /// <summary>Finds how far the face runs each way from its centre line.</summary>
        void MeasureFaceLimits(CoverSurface surf, Vector3 facePoint)
        {
            float max = 6f;
            _limitPos = Sweep(surf, facePoint, 1, max) - edgePadding;
            _limitNeg = -(Sweep(surf, facePoint, -1, max) - edgePadding);

            if (_limitPos < 0f)
            {
                _limitPos = 0f;
            }

            if (_limitNeg > 0f)
            {
                _limitNeg = 0f;
            }

            _along = Mathf.Clamp(_along, _limitNeg, _limitPos);
        }

        float Sweep(CoverSurface surf, Vector3 facePoint, int sign, float max)
        {
            float last = 0f;
            for (float d = cornerProbeStep; d <= max; d += cornerProbeStep)
            {
                Vector3 probe = facePoint + _faceTangent * (sign * d) + _faceNormal * 0.3f;
                bool stillWall = Physics.Raycast(probe, -_faceNormal, out RaycastHit h, 0.6f, coverMask,
                    QueryTriggerInteraction.Ignore);

                if (!stillWall || h.collider.GetComponentInParent<CoverSurface>() != surf)
                {
                    return last;
                }

                last = d;
            }

            return last;
        }

        // ---------------------------------------------------------------- maintain

        void Maintain(float dt)
        {
            if (Current == null || Current.Col == null)
            {
                Release();
                return;
            }

            // Standing up out of low cover breaks the hug, as asked.
            if (Mode == CoverMode.Low && motor.Stance != PlayerStance.Crouch)
            {
                Release();
                return;
            }

            Vector2 move = motor.MoveInput;
            Vector3 moveDir = Quaternion.Euler(0f, motor.CameraYaw, 0f) * new Vector3(move.x, 0f, move.y);
            moveDir.y = 0f;

            // Pulling away from the face lets go.
            if (Vector3.Dot(moveDir, _faceNormal) > 0.45f)
            {
                Release();
                motor.MoveDirect(_faceNormal * 0.2f);
                return;
            }

            if (GameInput.JumpPressed || GameInput.InteractPressed)
            {
                Release();
                return;
            }

            float slide = Vector3.Dot(moveDir, _faceTangent);
            _along = Mathf.Clamp(_along + slide * motor.coverSpeed * dt, _limitNeg, _limitPos);

            if (!ResolvePeek(slide, dt))
            {
                // Kept pushing past the corner: let go and carry straight on. The motor
                // picks the input up on this same frame so there is no hitch.
                Release();
                motor.MoveDirect(moveDir.normalized * (motor.coverSpeed * dt));
                return;
            }

            SnapToFace(PeekBlend * peekOutDistance * PeekSide, dt);
            FaceAlongCover(dt);
            UpdateWallHand();
        }

        /// <summary>Returns false when the player has pushed through the edge and should detach.</summary>
        bool ResolvePeek(float slide, float dt)
        {
            PeekSide = 0;
            _peekTarget = 0f;

            bool atPosEdge = _along >= _limitPos - 0.02f;
            bool atNegEdge = _along <= _limitNeg + 0.02f;
            bool pushingOut = (slide > 0.25f && atPosEdge) || (slide < -0.25f && atNegEdge);

            if (pushingOut)
            {
                int side = slide > 0f ? 1 : -1;
                PeekSide = side;
                _peekTarget = Mathf.Clamp01(Mathf.Abs(slide));

                // Aiming holds the lean. Just walking leans for a beat and then releases,
                // so you are never parked at a corner you are trying to walk around.
                if (!motor.IsAiming)
                {
                    _edgeHold += dt;
                    if (_edgeHold >= edgeReleaseDelay)
                    {
                        return false;
                    }
                }
                else
                {
                    _edgeHold = 0f;
                }
            }
            else
            {
                _edgeHold = 0f;
            }

            if (!pushingOut && motor.IsAiming && Mode == CoverMode.WallCorner && CornerSide != 0)
            {
                // Aiming at a corner leans out without needing to hold the stick into it.
                bool atCorner = CornerSide > 0 ? atPosEdge : atNegEdge;
                if (atCorner)
                {
                    PeekSide = CornerSide;
                    _peekTarget = 1f;
                }
            }

            PeekOver = false;
            if (Mode == CoverMode.Low && Current.allowPeekOver && PeekSide == 0)
            {
                if (motor.IsAiming || (cam != null && cam.Pitch > 12f))
                {
                    PeekOver = true;
                    _peekTarget = 1f;
                }
            }

            return true;
        }

        void SnapToFace(float lateralPeek, float dt)
        {
            Vector3 facePoint = Current.FacePlanePoint(_faceNormal, transform.position.y);
            Vector3 desired = facePoint
                              + _faceTangent * (_along + lateralPeek)
                              + _faceNormal * stickGap;

            // Critically damped glide onto the face. Sliding along the face is already
            // exact (we own _along), so this only ever has to absorb the initial approach
            // and the lean, which is what makes the attach read as a settle, not a snap.
            Vector3 current = transform.position;
            Vector3 next = Vector3.SmoothDamp(current, desired, ref _attachVel, attachSmoothing, Mathf.Infinity, dt);
            Vector3 delta = next - current;
            delta.y = 0f;

            float max = 0.12f + motor.coverSpeed * dt;
            if (delta.magnitude > max)
            {
                delta = delta.normalized * max;
            }

            motor.MoveDirect(delta);
        }

        /// <summary>
        /// Rests the wall-side hand on a tall wall at shoulder height, a little ahead of the
        /// body in the direction of travel so it reads as feeling the way along.
        /// </summary>
        void UpdateWallHand()
        {
            HasWallHand = false;
            if (Mode != CoverMode.WallCorner || Current == null)
            {
                return;
            }

            // Which hand is nearer the wall given the current facing.
            float rightDot = Vector3.Dot(transform.right, -_faceNormal);
            int side = rightDot >= 0f ? 1 : -1;

            float shoulderY = transform.position.y + Mathf.Max(0.9f, motor.Controller.height * 0.8f);
            Vector3 lead = _faceTangent * (CornerSide != 0 ? CornerSide * 0.18f : 0f);
            Vector3 origin = new Vector3(transform.position.x, shoulderY, transform.position.z)
                             + lead + _faceNormal * 0.1f;

            if (Physics.Raycast(origin, -_faceNormal, out RaycastHit hit, stickGap + 0.6f, coverMask, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform))
            {
                HasWallHand = true;
                WallHandPoint = hit.point + hit.normal * 0.015f;
                WallHandNormal = hit.normal;
                WallHandSide = side;
            }
        }

        void FaceAlongCover(float dt)
        {
            Vector3 facing;
            if (PeekSide != 0)
            {
                // Lean out: turn to look past the edge.
                Vector3 outward = _faceTangent * PeekSide;
                facing = Vector3.Slerp(-_faceNormal, (outward + _faceNormal).normalized, PeekBlend);
            }
            else if (Mode == CoverMode.WallCorner && CornerSide != 0)
            {
                // Hugging a wall: shoulder to it, facing the corner we are heading for.
                facing = _faceTangent * CornerSide;
            }
            else
            {
                facing = -_faceNormal;
            }

            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion target = Quaternion.LookRotation(facing.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-faceTurnSpeed * dt));
        }

        // ---------------------------------------------------------------- exit

        public void Release()
        {
            Mode = CoverMode.None;
            Current = null;
            PeekSide = 0;
            PeekOver = false;
            CornerSide = 0;
            _peekTarget = 0f;
            PeekBlend = 0f;
            _edgeHold = 0f;
            _attachVel = Vector3.zero;
            HasWallHand = false;

            if (motor != null)
            {
                motor.InCover = false;
            }

            _cooldown = detachCooldown;
        }

        /// <summary>Kept for the traversal system, which used the old name.</summary>
        public void ExitCover()
        {
            Release();
        }

        /// <summary>
        /// Where the weapon and its light should point while in cover: down the camera ray
        /// when leaning out, otherwise along the wall so the beam doesn't wash out the face.
        /// </summary>
        public Vector3 GetAimHint(Vector3 cameraForward)
        {
            if (!IsInCover)
            {
                return cameraForward;
            }

            if (PeekSide != 0 || PeekOver)
            {
                return cameraForward;
            }

            if (Mode == CoverMode.WallCorner && CornerSide != 0)
            {
                Vector3 alongWall = _faceTangent * CornerSide;
                return Vector3.Slerp(alongWall, cameraForward, 0.35f).normalized;
            }

            Vector3 lowReady = (_faceNormal * 0.35f + Vector3.down * 0.25f + cameraForward * 0.4f);
            return lowReady.sqrMagnitude > 0.0001f ? lowReady.normalized : cameraForward;
        }
    }
}
