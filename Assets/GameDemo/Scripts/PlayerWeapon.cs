using UnityEngine;

namespace GameDemo
{
    public enum LightMode
    {
        Flashlight,
        Lamp,
        Off
    }

    /// <summary>
    /// Handgun with an under-barrel light that switches between a focused flashlight beam and
    /// a soft lamp glow.
    ///
    /// The gun is not parented to the hand. Its pose is authored directly from the camera so
    /// the beam and the shot always agree with the crosshair, and the hands are then IK'd onto
    /// the grip. Weapon leads, hands follow.
    /// </summary>
    [DefaultExecutionOrder(55)]
    public class PlayerWeapon : MonoBehaviour
    {
        [Header("Refs")]
        public TlouPlayerMotor motor;
        public TlouCamera cam;
        public TlouCover cover;
        public HeroAvatar avatar;

        [Header("Light")]
        public LightMode lightMode = LightMode.Flashlight;
        public Color beamColor = new Color(1f, 0.95f, 0.84f);
        public float beamRange = 30f;
        public float beamAngle = 46f;
        public float beamIntensity = 4.6f;
        public float coreAngle = 16f;
        public float coreIntensity = 3.4f;

        [Header("Lamp")]
        public Color lampColor = new Color(1f, 0.74f, 0.44f);
        public float lampRange = 15f;
        public float lampIntensity = 2.6f;
        [Tooltip("Flicker depth of the lamp flame. 0 is a steady bulb.")]
        public float lampFlicker = 0.12f;

        [Header("Aim")]
        public float aimBlendSpeed = 10f;
        [Tooltip("Gun distance forward of the camera pivot while aiming.")]
        public float aimReach = 0.52f;
        public float aimRightOffset = 0.1f;
        public float aimDownOffset = 0.11f;

        [Header("Low Ready")]
        public float readyForward = 0.3f;
        public float readyRight = 0.16f;
        public float readyHeight = 1.06f;
        public float readyPitch = 38f;

        [Header("Fire")]
        public float fireRate = 4.5f;
        public float range = 60f;
        public float recoilPitch = 2.1f;
        public float recoilYaw = 0.5f;
        public LayerMask hitMask = ~0;

        public bool IsAiming { get; private set; }
        public float AimBlend { get; private set; }
        public Vector3 MuzzlePosition => _muzzle != null ? _muzzle.position : transform.position;
        /// <summary>World-space grip and support-hand points for hand IK on any rig.</summary>
        public Vector3 GripPosition => _grip != null ? _grip.position : transform.position;
        public Vector3 SupportPosition => _support != null ? _support.position : transform.position;
        public Quaternion GunRotation => _gun != null ? _gun.rotation : transform.rotation;
        public Vector3 AimDirection { get; private set; } = Vector3.forward;

        Transform _gun;
        Transform _muzzle;
        Transform _grip;
        Transform _support;
        Light _beam;
        Light _core;
        Light _lamp;
        Light _flash;
        Renderer _lampGlow;
        AnimatorDriver _animDriver;
        float _lampSeed;
        float _nextFire;
        float _flashUntil;
        Vector3 _kick;
        Vector3 _kickVel;
        Canvas _hud;
        RectTransform _reticle;

        void Awake()
        {
            if (motor == null) motor = GetComponent<TlouPlayerMotor>();
            if (cover == null) cover = GetComponent<TlouCover>();
            if (avatar == null) avatar = GetComponent<HeroAvatar>();
            if (cam == null) cam = FindFirstObjectByType<TlouCamera>();

            BuildGun();
            BuildHud();
            ApplyLightState();
        }

        // ------------------------------------------------------------------ build

        void BuildGun()
        {
            var root = new GameObject("Weapon_Handgun");
            _gun = root.transform;
            _gun.SetParent(transform, false);

            Material metal = SimpleMat(new Color(0.15f, 0.155f, 0.17f), 0.55f, 0.45f);
            Material grip = SimpleMat(new Color(0.1f, 0.09f, 0.085f), 0.05f, 0.2f);
            Material housing = SimpleMat(new Color(0.2f, 0.2f, 0.21f), 0.4f, 0.5f);
            Material lens = SimpleMat(new Color(0.85f, 0.85f, 0.75f), 0.1f, 0.9f);

            // Slide and frame along +Z.
            Piece(_gun, ShapeBuilder.TaperedBox(0.042f, 0.19f, 0.04f, 0.185f, 0.052f),
                new Vector3(0f, 0f, 0.028f), metal);
            Piece(_gun, ShapeBuilder.TaperedBox(0.038f, 0.1f, 0.036f, 0.095f, 0.03f),
                new Vector3(0f, -0.03f, -0.005f), housing);
            // Grip, raked back.
            var gripGo = Piece(_gun, ShapeBuilder.TaperedBox(0.034f, 0.055f, 0.032f, 0.05f, 0.115f),
                new Vector3(0f, -0.145f, -0.035f), grip);
            gripGo.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            // Trigger guard block.
            Piece(_gun, ShapeBuilder.TaperedBox(0.022f, 0.05f, 0.022f, 0.05f, 0.022f),
                new Vector3(0f, -0.048f, 0.0f), housing);
            // Under-barrel light body.
            Piece(_gun, ShapeBuilder.TaperedBox(0.032f, 0.09f, 0.032f, 0.085f, 0.028f),
                new Vector3(0f, -0.038f, 0.085f), housing);
            Piece(_gun, ShapeBuilder.TaperedBox(0.026f, 0.012f, 0.026f, 0.012f, 0.026f),
                new Vector3(0f, -0.038f, 0.128f), lens);
            // Sights.
            Piece(_gun, ShapeBuilder.TaperedBox(0.008f, 0.008f, 0.008f, 0.008f, 0.012f),
                new Vector3(0f, 0.052f, 0.108f), metal);
            Piece(_gun, ShapeBuilder.TaperedBox(0.022f, 0.01f, 0.022f, 0.01f, 0.013f),
                new Vector3(0f, 0.052f, -0.055f), metal);

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_gun, false);
            _muzzle.localPosition = new Vector3(0f, 0.008f, 0.125f);

            _grip = new GameObject("Grip").transform;
            _grip.SetParent(_gun, false);
            _grip.localPosition = new Vector3(0f, -0.075f, -0.028f);

            _support = new GameObject("Support").transform;
            _support.SetParent(_gun, false);
            _support.localPosition = new Vector3(-0.055f, -0.085f, 0.01f);

            // Wide beam plus a tight core reads much better in the dark than one cone.
            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(_gun, false);
            beamGo.transform.localPosition = new Vector3(0f, -0.038f, 0.1f);
            _beam = beamGo.AddComponent<Light>();
            _beam.type = LightType.Spot;
            _beam.range = beamRange;
            _beam.spotAngle = beamAngle;
            _beam.intensity = beamIntensity;
            _beam.color = beamColor;
            _beam.shadows = LightShadows.Soft;
            _beam.shadowStrength = 0.85f;
            _beam.renderMode = LightRenderMode.ForcePixel;

            var coreGo = new GameObject("BeamCore");
            coreGo.transform.SetParent(beamGo.transform, false);
            _core = coreGo.AddComponent<Light>();
            _core.type = LightType.Spot;
            _core.range = beamRange * 1.5f;
            _core.spotAngle = coreAngle;
            _core.intensity = coreIntensity;
            _core.color = beamColor;
            _core.shadows = LightShadows.None;
            _core.renderMode = LightRenderMode.ForcePixel;

            // Lamp mode: same housing, omnidirectional. Pushed out ahead of the muzzle so the
            // gun body does not carve a shadow wedge across everything in front of the player.
            var lampGo = new GameObject("Lamp");
            lampGo.transform.SetParent(_gun, false);
            lampGo.transform.localPosition = new Vector3(0f, -0.03f, 0.2f);
            _lamp = lampGo.AddComponent<Light>();
            _lamp.type = LightType.Point;
            _lamp.range = lampRange;
            _lamp.intensity = lampIntensity;
            _lamp.color = lampColor;
            _lamp.shadows = LightShadows.Soft;
            _lamp.shadowStrength = 0.55f;
            _lamp.renderMode = LightRenderMode.ForcePixel;
            _lampSeed = Random.value * 100f;

            _lampGlow = MakeGlobe(lampGo.transform);

            var flashGo = new GameObject("MuzzleFlash");
            flashGo.transform.SetParent(_muzzle, false);
            _flash = flashGo.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.range = 7f;
            _flash.intensity = 0f;
            _flash.color = new Color(1f, 0.82f, 0.55f);
            _flash.shadows = LightShadows.None;
        }

        static GameObject Piece(Transform parent, Mesh mesh, Vector3 localPos, Material mat)
        {
            var go = new GameObject("Part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Small emissive ball so lamp mode is visible on the weapon, not just on walls.</summary>
        Renderer MakeGlobe(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "LampGlobe";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.06f;
            Object.Destroy(go.GetComponent<Collider>());

            Material m = SimpleMat(lampColor, 0f, 0.3f);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", lampColor * 2.2f);
            }

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        static Material SimpleMat(Color c, float metallic, float smooth)
        {
            Shader s = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse");
            var m = new Material(s) { color = c };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        void BuildHud()
        {
            var go = new GameObject("WeaponHud");
            _hud = go.AddComponent<Canvas>();
            _hud.renderMode = RenderMode.ScreenSpaceOverlay;
            _hud.sortingOrder = 50;

            var dot = new GameObject("Reticle");
            dot.transform.SetParent(go.transform, false);
            var img = dot.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.9f, 0.95f, 0.95f, 0.75f);
            _reticle = img.rectTransform;
            _reticle.anchorMin = _reticle.anchorMax = new Vector2(0.5f, 0.5f);
            _reticle.sizeDelta = new Vector2(4f, 4f);
            _reticle.anchoredPosition = Vector2.zero;
            dot.SetActive(false);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (GameInput.LightPressed)
            {
                lightMode = lightMode switch
                {
                    LightMode.Flashlight => LightMode.Lamp,
                    LightMode.Lamp => LightMode.Off,
                    _ => LightMode.Flashlight
                };

                ApplyLightState();
            }

            bool canAim = motor != null && !motor.MovementLocked && motor.Stance != PlayerStance.Prone;
            IsAiming = canAim && GameInput.AimHeld && Cursor.lockState == CursorLockMode.Locked;

            if (motor != null)
            {
                motor.IsAiming = IsAiming;
                // Lit flashlight turns the body with the camera; lamp and off leave it free.
                motor.FaceCamera = lightMode == LightMode.Flashlight;
            }

            if (IsAiming && GameInput.FirePressed && Time.time >= _nextFire)
            {
                Fire();
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || cam == null || motor == null)
            {
                return;
            }

            AimBlend = Mathf.MoveTowards(AimBlend, IsAiming ? 1f : 0f, aimBlendSpeed * dt);
            _kick = Vector3.SmoothDamp(_kick, Vector3.zero, ref _kickVel, 0.09f, Mathf.Infinity, dt);

            PoseGun(dt);
            DriveHands();
            UpdateLamp();
            UpdateFlash();

            if (_reticle != null)
            {
                bool show = AimBlend > 0.5f;
                if (_reticle.gameObject.activeSelf != show)
                {
                    _reticle.gameObject.SetActive(show);
                }
            }
        }

        void PoseGun(float dt)
        {
            Transform camT = cam.transform;
            Vector3 lookDir = cam.LookForward;

            // Cover decides whether the gun stays tucked or comes up around the edge.
            if (cover != null && cover.IsInCover)
            {
                lookDir = cover.GetAimHint(lookDir);
            }

            AimDirection = lookDir.normalized;

            // Aimed: sit on the camera's line so the beam, the shot, and the dot agree.
            Vector3 aimPos = cam.PivotPoint
                             + AimDirection * aimReach
                             + camT.right * aimRightOffset
                             - camT.up * aimDownOffset;
            Quaternion aimRot = Quaternion.LookRotation(AimDirection, Vector3.up);

            // Low ready: held in against the body, angled down and outward.
            Vector3 bodyFwd = transform.forward;
            Vector3 readyPos = transform.position
                               + Vector3.up * (readyHeight * Mathf.Lerp(1f, 0.72f, 1f - motor.StanceFactor))
                               + bodyFwd * readyForward
                               + transform.right * readyRight;
            Vector3 flatAim = new Vector3(AimDirection.x, 0f, AimDirection.z);
            if (flatAim.sqrMagnitude < 1e-4f)
            {
                flatAim = bodyFwd;
            }

            Quaternion readyRot = Quaternion.LookRotation(flatAim.normalized, Vector3.up)
                                  * Quaternion.Euler(readyPitch, -14f, 0f);

            float t = Mathf.SmoothStep(0f, 1f, AimBlend);
            Vector3 pos = Vector3.Lerp(readyPos, aimPos, t) + _kick + StrideBob(t);
            Quaternion rot = Quaternion.Slerp(readyRot, aimRot, t);

            _gun.SetPositionAndRotation(pos, rot);
        }

        void DriveHands()
        {
            if (avatar == null)
            {
                return;
            }

            // Both hands on the grip while aiming; support hand relaxes at low ready.
            Vector3 right = _grip.position;
            Vector3 left = Vector3.Lerp(RelaxedLeftHand(), _support.position, Mathf.SmoothStep(0f, 1f, AimBlend));
            avatar.SetHandTargets(right, left);
        }

        /// <summary>
        /// Walking sway, driven off the avatar's stride phase rather than a free-running timer,
        /// so the gun rises and falls on the same beat as the footfalls. Aiming damps it down.
        /// </summary>
        Vector3 StrideBob(float aimT)
        {
            if (avatar == null)
            {
                return Vector3.zero;
            }

            float amount = avatar.MoveBlend * Mathf.Lerp(1f, 0.22f, aimT);
            if (amount < 0.001f)
            {
                return Vector3.zero;
            }

            float phase = avatar.GaitPhase;
            float vertical = -Mathf.Abs(Mathf.Cos(phase)) * 0.022f;
            float lateral = Mathf.Sin(phase) * 0.016f;

            return (Vector3.up * vertical + cam.transform.right * lateral) * amount;
        }

        /// <summary>Support hand at low ready: hangs by the hip and swings with the stride.</summary>
        Vector3 RelaxedLeftHand()
        {
            Transform chest = avatar != null && avatar.Chest != null ? avatar.Chest : transform;
            float swing = avatar != null ? Mathf.Sin(avatar.GaitPhase + Mathf.PI) * avatar.MoveBlend : 0f;

            return chest.position
                   - chest.up * 0.3f
                   - chest.right * 0.19f
                   + chest.forward * (0.1f + swing * 0.16f)
                   + chest.up * (swing * 0.04f);
        }

        void ApplyLightState()
        {
            bool torch = lightMode == LightMode.Flashlight;
            bool lamp = lightMode == LightMode.Lamp;

            if (_beam != null) _beam.enabled = torch;
            if (_core != null) _core.enabled = torch;
            if (_lamp != null) _lamp.enabled = lamp;
            if (_lampGlow != null) _lampGlow.enabled = lamp;
        }

        /// <summary>Slow flame wobble so the lamp does not read as a flat bulb.</summary>
        void UpdateLamp()
        {
            if (_lamp == null || !_lamp.enabled || lampFlicker <= 0f)
            {
                return;
            }

            float t = Time.time;
            float n = Mathf.PerlinNoise(_lampSeed, t * 1.9f) - 0.5f
                      + (Mathf.PerlinNoise(_lampSeed + 13f, t * 5.3f) - 0.5f) * 0.35f;
            _lamp.intensity = lampIntensity * (1f + n * 2f * lampFlicker);
        }

        void UpdateFlash()
        {
            if (_flash == null)
            {
                return;
            }

            _flash.intensity = Time.time < _flashUntil ? 6.5f : 0f;
        }

        void Fire()
        {
            _nextFire = Time.time + 1f / Mathf.Max(0.1f, fireRate);
            _flashUntil = Time.time + 0.05f;

            if (_animDriver == null) _animDriver = GetComponent<AnimatorDriver>();
            if (_animDriver != null) _animDriver.Fire();

            _kick = -AimDirection * 0.06f + Vector3.up * 0.02f;
            cam.AddRecoil(recoilPitch, Random.Range(-recoilYaw, recoilYaw));

            Vector3 origin = cam.PivotPoint + AimDirection * 0.2f;
            if (Physics.Raycast(origin, AimDirection, out RaycastHit hit, range, hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                SpawnImpact(hit.point, hit.normal);
            }
        }

        static void SpawnImpact(Vector3 point, Vector3 normal)
        {
            var go = new GameObject("Impact");
            go.transform.position = point + normal * 0.02f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.2f;
            l.intensity = 3.2f;
            l.color = new Color(1f, 0.86f, 0.6f);
            l.shadows = LightShadows.None;
            Destroy(go, 0.07f);
        }
    }
}
