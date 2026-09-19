using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Sits on the imported character's Animator (IK callbacks only fire on the Animator's own
    /// GameObject). Puts the hands on the weapon, drops the body into a crouch while pinning
    /// the feet so the legs bend instead of the boots sinking, and turns the head toward the
    /// camera look.
    ///
    /// The crouch here is a stand-in for real crouch clips: it produces a believable low
    /// stance from any standing locomotion clip, which is all the KayKit set provides.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(65)]
    public class HumanoidRig : MonoBehaviour
    {
        public TlouPlayerMotor motor;
        public PlayerWeapon weapon;
        public TlouCamera cam;
        public TlouCover cover;

        [Header("Crouch stand-in")]
        [Tooltip("Metres the hips drop at full crouch, in world units.")]
        public float crouchDrop = 0.34f;
        [Tooltip("Forward pitch spread across spine and chest at full crouch.")]
        public float crouchSpineBend = 26f;
        public float crouchBlendSpeed = 7f;

        [Header("Hands")]
        [Range(0f, 1f)] public float gunHandWeight = 1f;
        [Range(0f, 1f)] public float supportHandWeightAimed = 1f;
        [Range(0f, 1f)] public float supportHandWeightReady = 0f;

        [Header("Look")]
        [Range(0f, 1f)] public float lookWeight = 0.65f;
        public float lookBodyWeight = 0.2f;
        public float lookHeadWeight = 0.7f;

        Animator _anim;
        float _crouch;
        Transform _spine, _chest, _upperChest;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            if (motor == null) motor = GetComponentInParent<TlouPlayerMotor>();
            if (weapon == null && motor != null) weapon = motor.GetComponent<PlayerWeapon>();
            if (cover == null && motor != null) cover = motor.GetComponent<TlouCover>();
            if (cam == null) cam = FindFirstObjectByType<TlouCamera>();

            if (_anim.isHuman)
            {
                _spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
                _chest = _anim.GetBoneTransform(HumanBodyBones.Chest);
                _upperChest = _anim.GetBoneTransform(HumanBodyBones.UpperChest);
            }
        }

        void Update()
        {
            bool crouch = motor != null && motor.Stance == PlayerStance.Crouch;
            _crouch = Mathf.MoveTowards(_crouch, crouch ? 1f : 0f, crouchBlendSpeed * Time.deltaTime);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || !_anim.isHuman)
            {
                return;
            }

            if (_crouch > 0.001f)
            {
                // Read the animated foot goals before moving the body, then hold them there.
                Vector3 lf = _anim.GetIKPosition(AvatarIKGoal.LeftFoot);
                Vector3 rf = _anim.GetIKPosition(AvatarIKGoal.RightFoot);
                Quaternion lr = _anim.GetIKRotation(AvatarIKGoal.LeftFoot);
                Quaternion rr = _anim.GetIKRotation(AvatarIKGoal.RightFoot);

                _anim.bodyPosition += Vector3.down * (crouchDrop * _crouch);

                _anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, _crouch);
                _anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, _crouch);
                _anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, _crouch);
                _anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, _crouch);
                _anim.SetIKPosition(AvatarIKGoal.LeftFoot, lf);
                _anim.SetIKPosition(AvatarIKGoal.RightFoot, rf);
                _anim.SetIKRotation(AvatarIKGoal.LeftFoot, lr);
                _anim.SetIKRotation(AvatarIKGoal.RightFoot, rr);
            }

            if (weapon != null && weapon.enabled)
            {
                float aim = Mathf.SmoothStep(0f, 1f, weapon.AimBlend);
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, gunHandWeight);
                _anim.SetIKPosition(AvatarIKGoal.RightHand, weapon.GripPosition);

                float support = Mathf.Lerp(supportHandWeightReady, supportHandWeightAimed, aim);
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, support);
                _anim.SetIKPosition(AvatarIKGoal.LeftHand, weapon.SupportPosition);
            }

            if (cam != null && lookWeight > 0f)
            {
                bool inCover = cover != null && cover.IsInCover;
                float w = lookWeight * (inCover ? 0.5f : 1f);
                _anim.SetLookAtWeight(w, lookBodyWeight, lookHeadWeight, 0.9f, 0.6f);
                _anim.SetLookAtPosition(cam.PivotPoint + cam.LookForward * 8f);
            }
        }

        void LateUpdate()
        {
            if (_crouch <= 0.001f)
            {
                return;
            }

            // Fold the torso forward over the dropped hips so it reads as a crouch, not a squat.
            float total = crouchSpineBend * _crouch;
            int count = (_spine != null ? 1 : 0) + (_chest != null ? 1 : 0) + (_upperChest != null ? 1 : 0);
            if (count == 0)
            {
                return;
            }

            float each = total / count;
            Vector3 axis = transform.right;
            if (_spine != null) _spine.rotation = Quaternion.AngleAxis(each, axis) * _spine.rotation;
            if (_chest != null) _chest.rotation = Quaternion.AngleAxis(each, axis) * _chest.rotation;
            if (_upperChest != null) _upperChest.rotation = Quaternion.AngleAxis(each, axis) * _upperChest.rotation;
        }
    }
}
