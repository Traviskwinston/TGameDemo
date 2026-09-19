using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Third-person locomotion tuned for weight and momentum rather than instant response.
    ///
    /// Speed and heading are smoothed independently: heading turn rate falls off as speed
    /// rises, which is what produces wide arcs at a sprint and pivots on the spot at a walk.
    /// Acceleration and deceleration use separate rates so starts feel eager and stops carry.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(10)]
    public class TlouPlayerMotor : MonoBehaviour
    {
        public enum Gait
        {
            Idle,
            Walk,
            Jog,
            Sprint
        }

        [Header("Refs")]
        public TlouCamera cam;

        [Header("Speeds (m/s)")]
        public float walkSpeed = 1.55f;
        public float jogSpeed = 3.45f;
        public float sprintSpeed = 5.7f;
        public float crouchSpeed = 1.4f;
        public float proneSpeed = 0.75f;
        public float aimSpeed = 1.6f;
        public float coverSpeed = 1.35f;

        [Header("Accel / Decel")]
        [Tooltip("m/s^2 while speeding up.")]
        public float acceleration = 22f;
        [Tooltip("m/s^2 while slowing down. Lower than acceleration keeps stops weighty.")]
        public float deceleration = 14f;
        [Tooltip("Extra braking when input reverses hard, so sprint turnarounds plant.")]
        public float reverseBrake = 26f;
        public float airControl = 0.32f;

        [Header("Turning")]
        [Tooltip("deg/s when standing still or walking.")]
        public float turnRateSlow = 900f;
        [Tooltip("deg/s at full sprint. Low values make fast turns arc.")]
        public float turnRateFast = 300f;
        [Tooltip("deg/s the body turns to track the camera while the flashlight is lit.")]
        public float cameraFollowRate = 240f;
        [Tooltip("Camera can wander this far off the body before the lit body starts to follow.")]
        public float cameraFollowDeadZone = 6f;

        [Header("Jump / Gravity")]
        public float jumpHeight = 1.05f;
        public float gravity = -24f;
        public float coyoteTime = 0.12f;
        public float jumpBuffer = 0.12f;
        public float groundStick = -3f;

        [Header("Capsule")]
        public float standingHeight = 1.72f;
        public float crouchHeight = 1.05f;
        public float proneHeight = 0.5f;
        public float radius = 0.28f;

        [Header("Ground Probe")]
        public LayerMask groundMask = ~0;
        public float groundProbeExtra = 0.22f;

        public CharacterController Controller { get; private set; }
        public PlayerStance Stance { get; private set; } = PlayerStance.Standing;

        /// <summary>Planar velocity actually applied this frame.</summary>
        public Vector3 Velocity { get; private set; }
        public float Speed { get; private set; }
        public Gait CurrentGait { get; private set; }
        public bool Grounded { get; private set; }
        public bool IsSprinting => CurrentGait == Gait.Sprint;
        public bool MovementLocked { get; set; }

        /// <summary>Set by the cover system; cover owns planar motion while true.</summary>
        public bool InCover { get; set; }

        /// <summary>Set by the weapon; locks facing to the camera and forces strafe locomotion.</summary>
        public bool IsAiming { get; set; }

        /// <summary>
        /// Set by the weapon while the flashlight is lit. The body tracks the camera so the beam
        /// goes where you look, and locomotion becomes strafe/backpedal. With it off the camera
        /// orbits freely and the body only ever faces where it is walking.
        /// </summary>
        public bool FaceCamera { get; set; }

        /// <summary>Raw stick/WASD, already clamped to the unit circle.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>Local-space move direction relative to body facing. Drives strafe blends.</summary>
        public Vector2 LocalMove { get; private set; }

        /// <summary>Planar acceleration, for lean / bank on the avatar.</summary>
        public Vector3 PlanarAcceleration { get; private set; }

        /// <summary>Signed body turn rate in deg/s, for lean into turns.</summary>
        public float TurnVelocity { get; private set; }

        public Vector3 ExternalVelocity;

        /// <summary>Normalised eye height for the current stance. 1 = standing.</summary>
        public float StanceFactor => Mathf.InverseLerp(proneHeight, standingHeight, Controller.height);

        public float CameraYaw => cam != null ? cam.Yaw : transform.eulerAngles.y;

        Vector3 _heading = Vector3.forward;
        float _verticalVel;
        float _lastGroundedTime = -99f;
        float _jumpPressedTime = -99f;
        Vector3 _prevVelocity;
        Vector3 _groundNormal = Vector3.up;
        bool _crouchToggle;

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Controller.radius = radius;
            Controller.height = standingHeight;
            Controller.center = new Vector3(0f, standingHeight * 0.5f, 0f);
            Controller.slopeLimit = 50f;
            Controller.stepOffset = 0.35f;
            Controller.skinWidth = 0.025f;
            Controller.minMoveDistance = 0f;
            _heading = transform.forward;

            if (cam == null)
            {
                cam = FindFirstObjectByType<TlouCamera>();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            ProbeGround();
            ReadStanceInput();

            if (MovementLocked)
            {
                MoveInput = Vector2.zero;
                Velocity = Vector3.zero;
                Speed = 0f;
                CurrentGait = Gait.Idle;
                ApplyVertical(dt, Vector3.zero);
                return;
            }

            MoveInput = GameInput.Move;

            if (InCover)
            {
                // Cover slides the body along the face itself; we only own gravity here.
                Velocity = Vector3.zero;
                Speed = 0f;
                CurrentGait = Gait.Idle;
                LocalMove = Vector2.zero;
                ApplyVertical(dt, Vector3.zero);
                return;
            }

            UpdateLocomotion(dt);
        }

        void UpdateLocomotion(float dt)
        {
            Vector3 wish = Quaternion.Euler(0f, CameraYaw, 0f) * new Vector3(MoveInput.x, 0f, MoveInput.y);
            float inputMag = Mathf.Clamp01(wish.magnitude);
            if (inputMag > 0.001f)
            {
                wish /= inputMag;
            }

            CurrentGait = ResolveGait(inputMag);
            float targetSpeed = SpeedFor(CurrentGait) * inputMag;

            // --- heading: rotate the velocity direction, don't snap it ---
            if (inputMag > 0.01f)
            {
                float t = Mathf.InverseLerp(walkSpeed, sprintSpeed, Speed);
                float turnRate = Mathf.Lerp(turnRateSlow, turnRateFast, t);

                // Hard reversals get a brake instead of an impossible turn.
                bool reversing = Vector3.Dot(wish, _heading) < -0.35f && Speed > jogSpeed * 0.7f;
                if (reversing)
                {
                    targetSpeed = 0f;
                    turnRate = turnRateSlow;
                }

                _heading = Vector3.RotateTowards(_heading, wish, turnRate * Mathf.Deg2Rad * dt, 0f);
                _heading.y = 0f;
                _heading.Normalize();
            }

            // --- speed: asymmetric ramp ---
            float rate;
            if (targetSpeed > Speed)
            {
                rate = acceleration;
            }
            else if (inputMag < 0.01f)
            {
                rate = deceleration;
            }
            else
            {
                rate = reverseBrake;
            }

            if (!Grounded)
            {
                rate *= airControl;
            }

            Speed = Mathf.MoveTowards(Speed, targetSpeed, rate * dt);

            Vector3 planar = _heading * Speed;
            PlanarAcceleration = (planar - _prevVelocity) / dt;
            _prevVelocity = planar;
            Velocity = planar;

            ApplyFacing(dt, inputMag);

            Vector3 bodyRight = transform.right;
            Vector3 bodyFwd = transform.forward;
            float refSpeed = Mathf.Max(0.01f, sprintSpeed);
            LocalMove = new Vector2(Vector3.Dot(planar, bodyRight), Vector3.Dot(planar, bodyFwd)) / refSpeed;

            ApplyVertical(dt, planar);
        }

        Gait ResolveGait(float inputMag)
        {
            if (inputMag < 0.01f)
            {
                return Gait.Idle;
            }

            if (Stance != PlayerStance.Standing || IsAiming)
            {
                return Gait.Walk;
            }

            if (GameInput.SlowWalkHeld)
            {
                return Gait.Walk;
            }

            // Sprint needs a mostly-forward push, so strafing never reaches sprint speed.
            if (GameInput.SprintHeld && MoveInput.y > 0.25f)
            {
                return Gait.Sprint;
            }

            return Gait.Jog;
        }

        float SpeedFor(Gait gait)
        {
            if (Stance == PlayerStance.Prone)
            {
                return proneSpeed;
            }

            if (Stance == PlayerStance.Crouch)
            {
                return IsAiming ? Mathf.Min(aimSpeed, crouchSpeed) : crouchSpeed;
            }

            if (IsAiming)
            {
                return aimSpeed;
            }

            switch (gait)
            {
                case Gait.Walk: return walkSpeed;
                case Gait.Sprint: return sprintSpeed;
                case Gait.Jog: return jogSpeed;
                default: return 0f;
            }
        }

        void ApplyFacing(float dt, float inputMag)
        {
            float currentYaw = transform.eulerAngles.y;
            float targetYaw;
            float rate;

            // Sprinting always commits the body to the run direction, light or not.
            bool trackCamera = IsAiming || (FaceCamera && CurrentGait != Gait.Sprint);

            if (IsAiming)
            {
                // Aiming locks the chest to the camera; strafing reads off LocalMove.
                targetYaw = CameraYaw;
                rate = 720f;
            }
            else if (trackCamera)
            {
                // Light on: the body follows the camera so the beam goes where you look.
                float delta = Mathf.DeltaAngle(currentYaw, CameraYaw);
                if (Mathf.Abs(delta) < cameraFollowDeadZone && inputMag < 0.01f)
                {
                    TurnVelocity = Mathf.MoveTowards(TurnVelocity, 0f, 720f * dt);
                    return;
                }

                targetYaw = CameraYaw;
                rate = cameraFollowRate;
            }
            else if (inputMag > 0.01f)
            {
                targetYaw = Mathf.Atan2(_heading.x, _heading.z) * Mathf.Rad2Deg;
                float t = Mathf.InverseLerp(walkSpeed, sprintSpeed, Speed);
                rate = Mathf.Lerp(turnRateSlow, turnRateFast, t);
            }
            else
            {
                // Light off and standing still: the body keeps whatever facing it stopped
                // with. Back toward the camera and let go, you stay facing the camera. The
                // camera is free to orbit and never drags the body round.
                TurnVelocity = Mathf.MoveTowards(TurnVelocity, 0f, 720f * dt);
                return;
            }

            float newYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, rate * dt);
            TurnVelocity = Mathf.DeltaAngle(currentYaw, newYaw) / dt;
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

        void ApplyVertical(float dt, Vector3 planar)
        {
            if (Grounded)
            {
                _lastGroundedTime = Time.time;
            }

            if (GameInput.JumpPressed)
            {
                _jumpPressedTime = Time.time;
            }

            bool canJump = Time.time - _lastGroundedTime <= coyoteTime
                           && Time.time - _jumpPressedTime <= jumpBuffer
                           && Stance == PlayerStance.Standing
                           && !InCover
                           && !MovementLocked;

            if (canJump)
            {
                _verticalVel = Mathf.Sqrt(2f * jumpHeight * -gravity);
                _jumpPressedTime = -99f;
                _lastGroundedTime = -99f;
                Grounded = false;
            }
            else if (Grounded && _verticalVel <= 0f)
            {
                _verticalVel = groundStick;
            }
            else
            {
                _verticalVel += gravity * dt;
            }

            // Riding slopes instead of hopping down them.
            if (Grounded && _verticalVel <= 0f && _groundNormal.y > 0.1f && planar.sqrMagnitude > 0.0001f)
            {
                planar = Vector3.ProjectOnPlane(planar, _groundNormal).normalized * planar.magnitude;
            }

            Vector3 motion = planar + Vector3.up * _verticalVel + ExternalVelocity;
            ExternalVelocity = Vector3.zero;
            Controller.Move(motion * dt);
        }

        void ProbeGround()
        {
            // CharacterController.isGrounded flickers on edges and steps, so probe ourselves.
            float probe = Controller.skinWidth + groundProbeExtra;
            Vector3 origin = transform.position + Vector3.up * (radius + Controller.skinWidth + 0.02f);

            if (CastIgnoringSelf(origin, radius * 0.95f, Vector3.down, probe + 0.02f, out RaycastHit hit))
            {
                _groundNormal = hit.normal;
                Grounded = _verticalVel <= 0.01f && Vector3.Angle(hit.normal, Vector3.up) <= Controller.slopeLimit + 2f;
                return;
            }

            _groundNormal = Vector3.up;
            Grounded = false;
        }

        /// <summary>
        /// Sphere cast that skips our own capsule. The probe origin sits inside the controller,
        /// so an unfiltered cast can report the player standing on itself.
        /// </summary>
        bool CastIgnoringSelf(Vector3 origin, float castRadius, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, castRadius, dir, _hits, distance, groundMask,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.collider == Controller || h.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (h.distance < nearest)
                {
                    nearest = h.distance;
                    best = h;
                    found = true;
                }
            }

            return found;
        }

        readonly RaycastHit[] _hits = new RaycastHit[12];

        void ReadStanceInput()
        {
            if (InCover || MovementLocked)
            {
                return;
            }

            if (GameInput.CrouchPressed)
            {
                _crouchToggle = Stance != PlayerStance.Crouch;
                TrySetStance(_crouchToggle ? PlayerStance.Crouch : PlayerStance.Standing);
            }

            if (GameInput.PronePressed)
            {
                TrySetStance(Stance == PlayerStance.Prone ? PlayerStance.Crouch : PlayerStance.Prone);
            }
        }

        public bool TrySetStance(PlayerStance next)
        {
            if (Stance == next)
            {
                return true;
            }

            float h = HeightFor(next);
            if (h > Controller.height + 0.01f && !HasHeadroom(h))
            {
                return false;
            }

            Stance = next;
            Controller.height = h;
            Controller.center = new Vector3(0f, h * 0.5f, 0f);
            Controller.stepOffset = Mathf.Clamp(h * 0.2f, 0.08f, 0.35f);
            return true;
        }

        bool HasHeadroom(float wantedHeight)
        {
            float delta = wantedHeight - Controller.height;
            Vector3 top = transform.position + Vector3.up * (Controller.height - radius);
            return !CastIgnoringSelf(top, radius * 0.92f, Vector3.up, delta + 0.06f, out _);
        }

        float HeightFor(PlayerStance s)
        {
            switch (s)
            {
                case PlayerStance.Crouch: return crouchHeight;
                case PlayerStance.Prone: return proneHeight;
                default: return standingHeight;
            }
        }

        public void Teleport(Vector3 worldPos)
        {
            Controller.enabled = false;
            transform.position = worldPos;
            Controller.enabled = true;
        }

        /// <summary>Cover uses this to slide along a face without fighting our own velocity.</summary>
        public void MoveDirect(Vector3 worldDelta)
        {
            Controller.Move(worldDelta);
        }
    }
}
