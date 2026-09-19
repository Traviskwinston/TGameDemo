using System.Collections.Generic;
using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Feeds motor state into a humanoid Animator using the parameter names almost every
    /// third-person controller asset ships with. Only parameters the controller actually
    /// declares get written, so a partial controller still works instead of spamming errors.
    ///
    /// Add this alongside an imported rigged character to replace the procedural avatar.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class AnimatorDriver : MonoBehaviour
    {
        public TlouPlayerMotor motor;
        public TlouCover cover;
        public Animator animator;

        [Tooltip("Speed is written normalised 0..1 against sprint speed when true, raw m/s when false.")]
        public bool normalizeSpeed = true;
        public float damping = 0.12f;

        readonly HashSet<string> _params = new HashSet<string>();
        float _speed;
        float _moveX;
        float _moveY;
        Vector3 _prevPos;
        bool _hasPrev;

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<TlouPlayerMotor>();
            if (cover == null) cover = GetComponentInParent<TlouCover>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogWarning("AnimatorDriver: no Animator found; nothing to drive.", this);
                enabled = false;
                return;
            }

            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                _params.Add(p.name);
            }
        }

        void Update()
        {
            if (motor == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // Measure real displacement rather than trusting motor.Velocity: cover and
            // traversal move the body directly, and the legs should still animate there.
            Vector3 pos = motor.transform.position;
            Vector3 vel = _hasPrev ? (pos - _prevPos) / dt : Vector3.zero;
            _prevPos = pos;
            _hasPrev = true;
            vel.y = 0f;
            if (vel.magnitude > 12f) vel = Vector3.zero; // teleport

            float norm = normalizeSpeed ? Mathf.Max(0.01f, motor.sprintSpeed) : 1f;
            Vector3 local = motor.transform.InverseTransformDirection(vel) / norm;
            float target = vel.magnitude / norm;

            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, damping));
            _speed = Mathf.Lerp(_speed, target, k);
            _moveX = Mathf.Lerp(_moveX, local.x, k);
            _moveY = Mathf.Lerp(_moveY, local.z, k);

            SetFloat("Speed", _speed);
            SetFloat("MoveX", _moveX);
            SetFloat("MoveY", _moveY);
            SetFloat("Forward", _moveY);
            SetFloat("Strafe", _moveX);

            SetBool("Grounded", motor.Grounded);
            SetBool("Crouch", motor.Stance == PlayerStance.Crouch);
            SetBool("Crouching", motor.Stance == PlayerStance.Crouch);
            SetBool("Prone", motor.Stance == PlayerStance.Prone);
            SetBool("Aiming", motor.IsAiming);
            SetBool("Aim", motor.IsAiming);
            SetBool("Sprinting", motor.IsSprinting);
            SetBool("InCover", cover != null && cover.IsInCover);
        }

        /// <summary>Weapon hook: fires the shoot clip on the aim layer if the controller has it.</summary>
        public void Fire()
        {
            if (animator != null && _params.Contains("Fire"))
            {
                animator.SetTrigger("Fire");
            }
        }

        void SetFloat(string name, float value)
        {
            if (_params.Contains(name))
            {
                animator.SetFloat(name, value);
            }
        }

        void SetBool(string name, bool value)
        {
            if (_params.Contains(name))
            {
                animator.SetBool(name, value);
            }
        }
    }
}
