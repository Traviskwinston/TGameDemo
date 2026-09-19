using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Over-the-shoulder camera. Look input is read raw in Update so the motor sees it the
    /// same frame; framing is resolved in LateUpdate after the body has moved.
    ///
    /// Horizontal and vertical pivot follow are damped separately: stairs and step-offset
    /// pops are vertical, so a slower vertical spring removes the bobbing they cause without
    /// making lateral movement feel laggy.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TlouCamera : MonoBehaviour
    {
        [Header("Refs")]
        public Transform follow;
        public TlouCover cover;
        public TlouTraversal traversal;

        [Header("Framing")]
        public float shoulderOffset = 0.58f;
        public float pivotHeight = 1.46f;
        public float distance = 2.35f;
        public float normalFov = 64f;

        [Header("Aim")]
        public float aimShoulder = 0.42f;
        public float aimDistance = 1.25f;
        public float aimPivotHeight = 1.52f;
        public float aimFov = 46f;
        public float aimSensitivityScale = 0.6f;
        public float aimBlendSpeed = 9f;

        [Header("Crouch / Prone")]
        public float crouchPivotHeight = 0.98f;
        public float crouchDistance = 2.05f;
        public float pronePivotHeight = 0.42f;
        [Tooltip("Extra boom length at full sprint; the frame opens up as speed builds.")]
        public float sprintDistanceAdd = 0.32f;
        public float sprintFovAdd = 4f;
        public float crawlDistance = 1.15f;
        public float crawlFov = 54f;

        [Header("Look")]
        public float mouseSensitivity = 2.1f;
        public float minPitch = -55f;
        public float maxPitch = 62f;

        [Header("Damping")]
        public float lateralFollow = 0.06f;
        public float verticalFollow = 0.16f;
        public float rotationLerp = 26f;
        [Tooltip("Camera drifts this far along velocity so sprinting opens up the view ahead.")]
        public float leadAmount = 0.22f;

        [Header("Collision")]
        public float collisionRadius = 0.2f;
        public float minDistance = 0.45f;
        public float pushInSpeed = 40f;
        public float pullOutSpeed = 6f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float AimBlend { get; private set; }
        public int ShoulderSign { get; private set; } = 1;

        /// <summary>Flat forward on the horizontal plane. What "where the player is looking" means.</summary>
        public Vector3 FlatForward
        {
            get
            {
                Vector3 f = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        /// <summary>Full look ray including pitch. Weapons and the flashlight aim down this.</summary>
        public Vector3 LookForward => Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;

        public Vector3 PivotPoint => _pivot;

        Camera _cam;
        TlouPlayerMotor _motor;
        Vector3 _pivot;
        Vector3 _lateralVel;
        float _verticalVel;
        float _fovVel;
        float _currentDistance;
        float _distanceVel;
        float _shoulderBlend = 1f;
        float _recoilPitch;
        float _recoilYaw;
        float _recoilVel;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                _cam = gameObject.AddComponent<Camera>();
            }

            _cam.fieldOfView = normalFov;
            _cam.nearClipPlane = 0.04f;
            _currentDistance = distance;
            LockCursor(true);
        }

        public void Bind(TlouPlayerMotor motor, TlouCover coverSystem, TlouTraversal trav)
        {
            _motor = motor;
            follow = motor.transform;
            cover = coverSystem;
            traversal = trav;
            motor.cam = this;
            Yaw = follow.eulerAngles.y;
            _pivot = follow.position + Vector3.up * pivotHeight;
        }

        void Update()
        {
            if (traversal != null && traversal.IsBusy)
            {
                return;
            }

            HandleCursor();
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            float sens = mouseSensitivity * Mathf.Lerp(1f, aimSensitivityScale, AimBlend);
            Vector2 look = GameInput.Look;
            Yaw += look.x * sens;
            Pitch = Mathf.Clamp(Pitch - look.y * sens, minPitch, maxPitch);
        }

        void LateUpdate()
        {
            if (follow == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            bool aiming = _motor != null && _motor.IsAiming;
            AimBlend = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, aimBlendSpeed * dt);

            bool crawling = traversal != null && traversal.InCrawlSpace;
            ResolveShoulder(dt);

            float height = ResolvePivotHeight(crawling);
            float wantDistance = ResolveDistance(crawling);
            float shoulder = Mathf.Lerp(shoulderOffset, aimShoulder, AimBlend) * _shoulderBlend;

            // Pivot: split damping so vertical step pops don't shake the frame.
            Vector3 targetPivot = follow.position + Vector3.up * height;
            if (_motor != null && leadAmount > 0f)
            {
                targetPivot += _motor.Velocity * (leadAmount * (1f - AimBlend) / Mathf.Max(1f, _motor.sprintSpeed));
            }

            Vector3 flatPivot = new Vector3(_pivot.x, targetPivot.y, _pivot.z);
            Vector3 damped = Vector3.SmoothDamp(flatPivot, targetPivot, ref _lateralVel, lateralFollow, Mathf.Infinity, dt);
            _pivot.x = damped.x;
            _pivot.z = damped.z;
            _pivot.y = Mathf.SmoothDamp(_pivot.y, targetPivot.y, ref _verticalVel, verticalFollow, Mathf.Infinity, dt);

            UpdateRecoil(dt);
            Quaternion lookRot = Quaternion.Euler(Pitch + _recoilPitch, Yaw + _recoilYaw, 0f);
            Vector3 shoulderVec = lookRot * (Vector3.right * shoulder);
            Vector3 boomRoot = _pivot + shoulderVec;
            Vector3 boomDir = -(lookRot * Vector3.forward);

            float allowed = ResolveOcclusion(boomRoot, boomDir, wantDistance);
            float speed = allowed < _currentDistance ? pushInSpeed : pullOutSpeed;
            _currentDistance = Mathf.SmoothDamp(_currentDistance, allowed, ref _distanceVel, 1f / speed, Mathf.Infinity, dt);

            transform.position = boomRoot + boomDir * _currentDistance;
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationLerp * dt);

            float wantFov = crawling ? crawlFov : Mathf.Lerp(normalFov + sprintFovAdd * SprintBlend(), aimFov, AimBlend);
            _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, wantFov, ref _fovVel, 0.18f);
        }

        void ResolveShoulder(float dt)
        {
            float desired = 1f;
            if (cover != null && cover.IsInCover && cover.PeekSide != 0)
            {
                // Peeking right means the camera goes to the left shoulder, and the reverse,
                // so the body never blocks the thing you leaned out to see.
                desired = cover.PeekSide > 0 ? -1f : 1f;
            }

            _shoulderBlend = Mathf.MoveTowards(_shoulderBlend, desired, 6f * dt);
            ShoulderSign = _shoulderBlend >= 0f ? 1 : -1;
        }

        float ResolvePivotHeight(bool crawling)
        {
            float standing = Mathf.Lerp(pivotHeight, aimPivotHeight, AimBlend);
            if (_motor == null)
            {
                return standing;
            }

            if (crawling || _motor.Stance == PlayerStance.Prone)
            {
                return pronePivotHeight;
            }

            if (_motor.Stance == PlayerStance.Crouch)
            {
                return crouchPivotHeight + (aimPivotHeight - pivotHeight) * AimBlend;
            }

            return standing;
        }

        float ResolveDistance(bool crawling)
        {
            if (crawling)
            {
                return crawlDistance;
            }

            float relaxed = distance + sprintDistanceAdd * SprintBlend();
            if (_motor != null && _motor.Stance == PlayerStance.Crouch)
            {
                relaxed = crouchDistance;
            }

            return Mathf.Lerp(relaxed, aimDistance, AimBlend);
        }

        /// <summary>0 at jog or below, 1 at full sprint speed.</summary>
        float SprintBlend()
        {
            if (_motor == null)
            {
                return 0f;
            }

            return Mathf.InverseLerp(_motor.jogSpeed, _motor.sprintSpeed, _motor.Speed);
        }

        float ResolveOcclusion(Vector3 root, Vector3 dir, float wanted)
        {
            if (Physics.SphereCast(root, collisionRadius, dir, out RaycastHit hit, wanted,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(minDistance, hit.distance - 0.05f);
            }

            return wanted;
        }

        void UpdateRecoil(float dt)
        {
            _recoilPitch = Mathf.SmoothDamp(_recoilPitch, 0f, ref _recoilVel, 0.12f, Mathf.Infinity, dt);
            _recoilYaw = Mathf.MoveTowards(_recoilYaw, 0f, 18f * dt);
        }

        /// <summary>Called by the weapon on each shot.</summary>
        public void AddRecoil(float pitchKick, float yawKick)
        {
            _recoilPitch -= pitchKick;
            _recoilYaw += yawKick;
        }

        void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
            }
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor(true);
            }
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
