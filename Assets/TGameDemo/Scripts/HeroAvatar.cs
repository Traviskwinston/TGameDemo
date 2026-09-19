using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Procedural hooded protagonist. Small frame, dark-fantasy read, stylised in the way
    /// Valheim is: chunky tapered segments joined by spheres so knees and elbows never open
    /// up, boots that actually plant, and a cape that hangs off a shoulder mantle as one
    /// simulated sheet.
    ///
    /// Locomotion is not a looping animation. Each foot is either planted (locked to a world
    /// point until it is lifted) or swinging along an arc to a predicted landing. Because the
    /// swing is driven by distance travelled, feet cannot slide and footfalls stay in sync
    /// with the real speed. Legs and arms are solved with two-bone IK onto those targets.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class HeroAvatar : MonoBehaviour
    {
        [Header("Refs")]
        public TlouPlayerMotor motor;
        public TlouCamera cam;
        public TlouCover cover;

        [Header("Appearance")]
        public float bodyHeight = 1.72f;
        public Color cloakColor = new Color(0.15f, 0.17f, 0.2f);
        public Color hoodLining = new Color(0.05f, 0.05f, 0.06f);
        public Color tunicColor = new Color(0.22f, 0.2f, 0.18f);
        public Color trouserColor = new Color(0.17f, 0.16f, 0.15f);
        public Color leatherColor = new Color(0.28f, 0.2f, 0.13f);
        public Color strapColor = new Color(0.12f, 0.1f, 0.08f);
        public Color skinColor = new Color(0.76f, 0.62f, 0.5f);
        public Color runeColor = new Color(0.2f, 0.85f, 0.75f);

        [Header("Gait (cycle length = distance for two steps, m)")]
        public float walkCycle = 1.3f;
        public float jogCycle = 1.95f;
        public float sprintCycle = 2.7f;
        public float crouchCycle = 1.05f;
        [Tooltip("Fraction of the cycle a foot spends in the air, walk -> sprint.")]
        public Vector2 swingFraction = new Vector2(0.42f, 0.58f);
        [Tooltip("Foot lift height, walk -> sprint.")]
        public Vector2 stepHeight = new Vector2(0.06f, 0.15f);
        public float footSpacing = 0.11f;
        public float idleStepThreshold = 0.2f;
        public float idleStepDuration = 0.26f;

        [Header("Body Motion")]
        public float bobAmount = 0.03f;
        public float leanStrength = 1.6f;
        public float maxLean = 12f;
        public float bankStrength = 0.03f;
        public float maxBank = 8f;
        public float armSwingDegrees = 32f;

        [Header("Cape")]
        public int capeRows = 8;
        public float capeLength = 0.72f;
        public float capeDamping = 0.965f;
        public float capeGravity = 6.5f;

        // ---------------------------------------------------------------- public API

        public Transform GunHand => _wristR;
        public Transform Head => _head;
        public Transform Chest => _chest;

        /// <summary>Current gait phase in radians. The weapon bobs on it.</summary>
        public float GaitPhase => _phase;

        /// <summary>0 standing still, 1 fully into the locomotion cycle.</summary>
        public float MoveBlend => _moveBlend;

        public void SetHandTargets(Vector3 rightHand, Vector3 leftHand)
        {
            _handTargetR = rightHand;
            _handTargetL = leftHand;
            _hasHandTargets = true;
        }

        public void ClearHandTargets()
        {
            _hasHandTargets = false;
        }

        // ---------------------------------------------------------------- skeleton

        // Joints are plain transforms positioned in world space every frame.
        Transform _rig;
        Transform _pelvis, _chest, _neck, _head;
        Transform _shoulderL, _shoulderR, _elbowL, _elbowR, _wristL, _wristR;
        Transform _hipL, _hipR, _kneeL, _kneeR, _ankleL, _ankleR;

        // Visual segments hang from their top joint toward the next one.
        Transform _visThighL, _visThighR, _visShinL, _visShinR;
        Transform _visUpperL, _visUpperR, _visForeL, _visForeR;
        Transform _visTorso, _visPelvis, _visMantle, _visHood, _visHoodLining;

        // Proportions (scaled from bodyHeight / 1.72).
        float _s;
        float _hipY, _thigh, _shin, _ankleH;
        float _pelvisHalfW, _torsoLen, _shoulderHalfW, _upperArm, _foreArm;
        float _legLen, _armLen;

        // ---------------------------------------------------------------- state

        float _phase;
        float _moveBlend;
        Vector3 _prevPos;
        Vector3 _vel;
        Vector3 _smoothAccel;
        float _lean, _bank;
        float _pelvisDrop;

        bool _hasHandTargets;
        Vector3 _handTargetR, _handTargetL;
        Vector3 _wallHandPos;
        Vector3 _wallHandVel;
        float _wallHandBlend;

        Vector3 _headLook;

        struct Foot
        {
            public Vector3 pos;
            public Vector3 planted;
            public Vector3 liftFrom;
            public Vector3 target;
            public float yaw;
            public float plantedYaw;
            public float liftYaw;
            public bool swinging;
            public float swingT;
            public bool idleStepping;
            public float idleT;
        }

        Foot _footL, _footR;
        int _lastIdleFoot = 1;

        // Cape.
        Vector3[] _capePos, _capePrev;
        float[] _capeRowGap;
        float[] _capeColGap;
        Mesh _capeMesh;
        Transform _capeRoot;
        Vector3[] _capeVerts;
        const int CapeCols = 4;

        Material _matCloak, _matLining, _matTunic, _matTrouser, _matLeather, _matStrap, _matSkin, _matRune;
        static Mesh _sphereMesh;

        // ================================================================ setup

        bool _built;

        void Awake()
        {
            if (motor == null) motor = GetComponent<TlouPlayerMotor>();
            if (cover == null) cover = GetComponent<TlouCover>();
            if (cam == null) cam = FindFirstObjectByType<TlouCamera>();
        }

        // Built on enable, not Awake: Awake still runs on a disabled component, and an
        // imported character disables this one. Building there would leave the procedural
        // body standing inside the imported model.
        void OnEnable()
        {
            if (_built)
            {
                if (_rig != null) _rig.gameObject.SetActive(true);
                return;
            }

            StripLegacyVisuals();
            ComputeProportions();
            BuildMaterials();
            BuildSkeleton();
            BuildVisuals();
            BuildCape();

            _prevPos = transform.position;
            ResetPose();
            _built = true;
        }

        void OnDisable()
        {
            if (_rig != null) _rig.gameObject.SetActive(false);
        }

        void StripLegacyVisuals()
        {
            foreach (string n in new[] { "Visual", "Rig", "HeroRig", "FloatingHand" })
            {
                Transform t = transform.Find(n);
                if (t != null)
                {
                    Destroy(t.gameObject);
                }
            }
        }

        void ComputeProportions()
        {
            _s = bodyHeight / 1.72f;
            _hipY = 0.9f * _s;
            _ankleH = 0.05f * _s;
            _thigh = 0.43f * _s;
            _shin = (_hipY - _ankleH - _thigh) * 1.005f; // hair of slack so the knee always bends forward
            _legLen = _thigh + _shin;

            _pelvisHalfW = 0.09f * _s;
            _torsoLen = 0.42f * _s;
            _shoulderHalfW = 0.185f * _s;
            _upperArm = 0.28f * _s;
            _foreArm = 0.27f * _s;
            _armLen = _upperArm + _foreArm;
        }

        void BuildMaterials()
        {
            _matCloak = Mat(cloakColor, 0f, 0.18f);
            _matLining = Mat(hoodLining, 0f, 0.05f);
            _matTunic = Mat(tunicColor, 0f, 0.22f);
            _matTrouser = Mat(trouserColor, 0f, 0.2f);
            _matLeather = Mat(leatherColor, 0.05f, 0.42f);
            _matStrap = Mat(strapColor, 0.1f, 0.35f);
            _matSkin = Mat(skinColor, 0f, 0.3f);
            _matRune = Mat(runeColor, 0f, 0.6f);
            if (_matRune.HasProperty("_EmissionColor"))
            {
                _matRune.EnableKeyword("_EMISSION");
                _matRune.SetColor("_EmissionColor", runeColor * 1.6f);
            }
        }

        static Material Mat(Color c, float metallic, float smooth)
        {
            Shader s = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse");
            var m = new Material(s) { color = c };
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        void BuildSkeleton()
        {
            _rig = new GameObject("HeroRig").transform;
            _rig.SetParent(transform, false);

            _pelvis = Joint("Pelvis");
            _chest = Joint("Chest");
            _neck = Joint("Neck");
            _head = Joint("Head");
            _shoulderL = Joint("Shoulder_L"); _shoulderR = Joint("Shoulder_R");
            _elbowL = Joint("Elbow_L"); _elbowR = Joint("Elbow_R");
            _wristL = Joint("Hand_L"); _wristR = Joint("Hand_R");
            _hipL = Joint("Hip_L"); _hipR = Joint("Hip_R");
            _kneeL = Joint("Knee_L"); _kneeR = Joint("Knee_R");
            _ankleL = Joint("Ankle_L"); _ankleR = Joint("Ankle_R");
        }

        Transform Joint(string n)
        {
            var t = new GameObject(n).transform;
            t.SetParent(_rig, false);
            return t;
        }

        void BuildVisuals()
        {
            float s = _s;

            // --- pelvis + torso -------------------------------------------------
            _visPelvis = Part("Pelvis_V", ShapeBuilder.TaperedBox(0.28f * s, 0.19f * s, 0.26f * s, 0.18f * s, 0.14f * s), _matTrouser);
            Sub(_visPelvis, ShapeBuilder.CenteredBox(0.3f * s, 0.05f * s, 0.21f * s), new Vector3(0f, 0.12f * s, 0f), _matLeather); // belt

            _visTorso = Part("Torso_V", ShapeBuilder.TaperedBox(0.25f * s, 0.17f * s, 0.34f * s, 0.2f * s, _torsoLen), _matTunic);
            // Chest strap and a small rune clasp.
            var strap = Sub(_visTorso, ShapeBuilder.CenteredBox(0.06f * s, 0.34f * s, 0.215f * s), new Vector3(-0.05f * s, _torsoLen * 0.52f, 0f), _matStrap);
            strap.localRotation = Quaternion.Euler(0f, 0f, 22f);
            Sub(_visTorso, ShapeBuilder.CenteredBox(0.045f * s, 0.045f * s, 0.02f * s), new Vector3(-0.02f * s, _torsoLen * 0.72f, 0.105f * s), _matRune);

            // Mantle over the shoulders: what the cape hangs from.
            _visMantle = Part("Mantle_V", ShapeBuilder.TaperedBox(0.46f * s, 0.3f * s, 0.3f * s, 0.24f * s, 0.11f * s), _matCloak);

            // --- head + hood ------------------------------------------------------
            Sub(_head, ShapeBuilder.CenteredBox(0.17f * s, 0.2f * s, 0.19f * s), Vector3.zero, _matSkin);
            _visHood = Part("Hood_V", ShapeBuilder.Hood(0.31f * s, 0.32f * s, 0.34f * s), _matCloak);
            _visHoodLining = Part("HoodLining_V", ShapeBuilder.Hood(0.29f * s, 0.3f * s, 0.325f * s, true), _matLining);

            // --- limbs ------------------------------------------------------------
            _visThighL = Limb("Thigh_L", 0.13f, 0.1f, _thigh, _matTrouser);
            _visThighR = Limb("Thigh_R", 0.13f, 0.1f, _thigh, _matTrouser);
            _visShinL = Limb("Shin_L", 0.1f, 0.075f, _shin, _matTrouser);
            _visShinR = Limb("Shin_R", 0.1f, 0.075f, _shin, _matTrouser);
            _visUpperL = Limb("Upper_L", 0.095f, 0.08f, _upperArm, _matTunic);
            _visUpperR = Limb("Upper_R", 0.095f, 0.08f, _upperArm, _matTunic);
            _visForeL = Limb("Fore_L", 0.08f, 0.065f, _foreArm, _matLeather);
            _visForeR = Limb("Fore_R", 0.08f, 0.065f, _foreArm, _matLeather);

            // Joints: spheres sized to the segments meeting there. This is what closes the gaps.
            Ball(_hipL, 0.07f * s, _matTrouser); Ball(_hipR, 0.07f * s, _matTrouser);
            Ball(_kneeL, 0.056f * s, _matTrouser); Ball(_kneeR, 0.056f * s, _matTrouser);
            Ball(_shoulderL, 0.062f * s, _matTunic); Ball(_shoulderR, 0.062f * s, _matTunic);
            Ball(_elbowL, 0.046f * s, _matLeather); Ball(_elbowR, 0.046f * s, _matLeather);
            Ball(_wristL, 0.036f * s, _matLeather); Ball(_wristR, 0.036f * s, _matLeather);

            // Hands: pivot at the wrist, palm along the forearm direction (local -Y).
            Sub(_wristL, HandMesh(s), Vector3.zero, _matSkin);
            Sub(_wristR, HandMesh(s), Vector3.zero, _matSkin);

            // Boots: pivot at the ankle, cuff wrapping the joint.
            Sub(_ankleL, ShapeBuilder.Foot(0.095f * s, 0.1f * s, 0.18f * s, 0.07f * s), Vector3.zero, _matLeather);
            Sub(_ankleR, ShapeBuilder.Foot(0.095f * s, 0.1f * s, 0.18f * s, 0.07f * s), Vector3.zero, _matLeather);
        }

        static Mesh HandMesh(float s)
        {
            Mesh m = ShapeBuilder.TaperedBox(0.07f * s, 0.032f * s, 0.055f * s, 0.028f * s, 0.09f * s);
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                v[i].y -= 0.09f * s; // hang from the wrist
            }

            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }

        Transform Part(string n, Mesh mesh, Material mat)
        {
            var go = new GameObject(n);
            go.transform.SetParent(_rig, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Transform Sub(Transform parent, Mesh mesh, Vector3 localPos, Material mat)
        {
            var go = new GameObject("Sub");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        Transform Limb(string n, float top, float bottom, float length, Material mat)
        {
            return Part(n, ShapeBuilder.Limb(top * _s, bottom * _s, length), mat);
        }

        static void Ball(Transform joint, float radius, Material mat)
        {
            if (_sphereMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }

            var go = new GameObject("Joint");
            go.transform.SetParent(joint, false);
            go.transform.localScale = Vector3.one * (radius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = _sphereMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildCape()
        {
            int rows = Mathf.Max(3, capeRows);
            _capePos = new Vector3[rows * CapeCols];
            _capePrev = new Vector3[rows * CapeCols];
            _capeRowGap = new float[rows];
            _capeColGap = new float[rows];

            float gap = capeLength / (rows - 1);
            for (int r = 0; r < rows; r++)
            {
                _capeRowGap[r] = gap;
                // Flares out toward the hem.
                float halfW = Mathf.Lerp(0.14f, 0.24f, r / (float)(rows - 1)) * _s;
                _capeColGap[r] = (halfW * 2f) / (CapeCols - 1);
            }

            var go = new GameObject("Cape_V");
            _capeRoot = go.transform;
            _capeRoot.SetParent(_rig, false);
            _capeMesh = new Mesh { name = "Cape" };
            _capeMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _capeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _matCloak;

            int n = rows * CapeCols;
            _capeVerts = new Vector3[n * 2];
            var tris = new int[(rows - 1) * (CapeCols - 1) * 12];
            int t = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < CapeCols - 1; c++)
                {
                    int a = r * CapeCols + c;
                    int b = a + 1;
                    int d = a + CapeCols;
                    int e = d + 1;
                    // Front (outside) face.
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                    // Back face uses the duplicated vertex set with reversed winding.
                    tris[t++] = n + a; tris[t++] = n + b; tris[t++] = n + d;
                    tris[t++] = n + b; tris[t++] = n + e; tris[t++] = n + d;
                }
            }

            _capeMesh.vertices = _capeVerts;
            _capeMesh.triangles = tris;
        }

        // ================================================================ pose

        void ResetPose()
        {
            Vector3 p = transform.position;
            Vector3 f = transform.forward;
            Vector3 r = transform.right;
            float yaw = transform.eulerAngles.y;

            _footL.pos = _footL.planted = GroundAt(p - r * footSpacing) + Vector3.up * _ankleH;
            _footR.pos = _footR.planted = GroundAt(p + r * footSpacing) + Vector3.up * _ankleH;
            _footL.yaw = _footL.plantedYaw = yaw - 4f;
            _footR.yaw = _footR.plantedYaw = yaw + 4f;

            Vector3 chest = p + Vector3.up * (_hipY + 0.08f * _s + _torsoLen);
            int rows = _capeRowGap.Length;
            for (int r0 = 0; r0 < rows; r0++)
            {
                for (int c = 0; c < CapeCols; c++)
                {
                    float x = (c - (CapeCols - 1) * 0.5f) * _capeColGap[r0];
                    Vector3 pos = chest + r * x - f * (0.12f * _s + r0 * 0.01f) - Vector3.up * (r0 * _capeRowGap[r0]);
                    _capePos[r0 * CapeCols + c] = pos;
                    _capePrev[r0 * CapeCols + c] = pos;
                }
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || motor == null || !_built)
            {
                return;
            }

            // Own velocity from actual displacement: cover and traversal move the body
            // without touching motor.Velocity, and feet must plant correctly there too.
            Vector3 pos = transform.position;
            Vector3 rawVel = (pos - _prevPos) / dt;
            rawVel.y = 0f;
            _prevPos = pos;
            if (rawVel.magnitude > 12f) rawVel = Vector3.zero; // teleport
            _vel = Vector3.Lerp(_vel, rawVel, 1f - Mathf.Exp(-18f * dt));
            float speed = _vel.magnitude;
            bool moving = speed > 0.12f;

            bool inCover = cover != null && cover.IsInCover;
            float stance = StanceBlend(out bool prone);

            AdvanceGait(speed, moving, dt);

            // --- pelvis --------------------------------------------------------
            Vector3 fwd = transform.forward;
            Vector3 right = transform.right;
            float hipY = Mathf.Lerp(0.3f * _s, Mathf.Lerp(0.62f * _s, _hipY, stance), prone ? 0f : 1f);

            float bob = -bobAmount * Mathf.Abs(Mathf.Cos(_phase)) * _moveBlend * Mathf.Lerp(0.6f, 1.4f, SpeedT());
            float sway = Mathf.Sin(_phase) * 0.012f * _moveBlend;
            Vector3 pelvisPos = pos + Vector3.up * (hipY + bob - _pelvisDrop) + right * sway;

            ApplyLean(dt, speed);
            Quaternion pelvisRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Quaternion.Euler(0f, 0f, -_bank);
            // Hips counter-rotate a little against the stride.
            pelvisRot *= Quaternion.Euler(0f, Mathf.Sin(_phase) * 6f * _moveBlend, 0f);

            float crouchPitch = prone ? 78f : Mathf.Lerp(26f, 0f, stance);
            Quaternion chestRot = pelvisRot
                                  * Quaternion.Euler(_lean + crouchPitch, -Mathf.Sin(_phase) * 5f * _moveBlend, 0f);

            Vector3 waist = pelvisPos + pelvisRot * Vector3.up * (0.08f * _s);
            Vector3 chestPos = waist + chestRot * Vector3.up * _torsoLen;
            Vector3 shoulderL = chestPos + chestRot * new Vector3(-_shoulderHalfW, -0.015f * _s, 0f);
            Vector3 shoulderR = chestPos + chestRot * new Vector3(_shoulderHalfW, -0.015f * _s, 0f);
            Vector3 neckPos = chestPos + chestRot * Vector3.up * (0.05f * _s);

            _pelvis.SetPositionAndRotation(pelvisPos, pelvisRot);
            _chest.SetPositionAndRotation(chestPos, chestRot);
            _neck.SetPositionAndRotation(neckPos, chestRot);
            _shoulderL.position = shoulderL;
            _shoulderR.position = shoulderR;

            // --- feet + legs ---------------------------------------------------
            Vector3 hipL = pelvisPos + pelvisRot * new Vector3(-_pelvisHalfW, 0f, 0f);
            Vector3 hipR = pelvisPos + pelvisRot * new Vector3(_pelvisHalfW, 0f, 0f);
            _hipL.position = hipL;
            _hipR.position = hipR;

            UpdateFoot(ref _footL, -1, hipL, moving, dt, 0f);
            UpdateFoot(ref _footR, 1, hipR, moving, dt, Mathf.PI);
            IdleSteps(moving, dt, pos, right, fwd);
            ResolvePelvisDrop(hipL, hipR, dt);

            SolveLeg(hipL, _footL, _kneeL, _ankleL, _visThighL, _visShinL, fwd, right, -1);
            SolveLeg(hipR, _footR, _kneeR, _ankleR, _visThighR, _visShinR, fwd, right, 1);

            // --- arms ----------------------------------------------------------
            ResolveWallHand(dt, inCover);
            Vector3 handL, handR;
            if (_hasHandTargets)
            {
                handR = _handTargetR;
                handL = _handTargetL;
            }
            else
            {
                handR = ProceduralHand(shoulderR, chestRot, 1);
                handL = ProceduralHand(shoulderL, chestRot, -1);
            }

            if (_wallHandBlend > 0.001f)
            {
                handL = Vector3.Lerp(handL, _wallHandPos, Mathf.SmoothStep(0f, 1f, _wallHandBlend));
            }

            SolveArm(shoulderL, handL, chestRot, -1, _elbowL, _wristL, _visUpperL, _visForeL);
            SolveArm(shoulderR, handR, chestRot, 1, _elbowR, _wristR, _visUpperR, _visForeR);

            // --- head ----------------------------------------------------------
            ApplyHead(neckPos, chestRot, dt, inCover);

            // --- body shells ---------------------------------------------------
            _visPelvis.SetPositionAndRotation(pelvisPos - pelvisRot * Vector3.up * (0.06f * _s), pelvisRot);
            _visTorso.SetPositionAndRotation(waist, chestRot);
            _visMantle.SetPositionAndRotation(chestPos - chestRot * Vector3.up * (0.07f * _s), chestRot);

            SimulateCape(chestPos, chestRot, pelvisPos, pelvisRot, pos.y, dt);

            _hasHandTargets = false;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = _footL.swinging ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(_footL.pos, 0.04f);
            Gizmos.color = _footR.swinging ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(_footR.pos, 0.04f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(_footL.target, 0.02f);
            Gizmos.DrawSphere(_footR.target, 0.02f);
        }

        // ---------------------------------------------------------------- gait

        float StanceBlend(out bool prone)
        {
            prone = motor.Stance == PlayerStance.Prone;
            float h = motor.Controller != null ? motor.Controller.height : motor.standingHeight;
            return Mathf.Clamp01(Mathf.InverseLerp(motor.crouchHeight, motor.standingHeight, h));
        }

        float SpeedT()
        {
            return Mathf.InverseLerp(motor.walkSpeed, motor.sprintSpeed, _vel.magnitude);
        }

        float CycleLength()
        {
            if (motor.Stance != PlayerStance.Standing)
            {
                return crouchCycle * _s;
            }

            float v = _vel.magnitude;
            float c = v <= motor.jogSpeed
                ? Mathf.Lerp(walkCycle, jogCycle, Mathf.InverseLerp(motor.walkSpeed, motor.jogSpeed, v))
                : Mathf.Lerp(jogCycle, sprintCycle, Mathf.InverseLerp(motor.jogSpeed, motor.sprintSpeed, v));
            return c * _s;
        }

        float SwingFrac() => Mathf.Lerp(swingFraction.x, swingFraction.y, SpeedT());
        float StepHeight() => Mathf.Lerp(stepHeight.x, stepHeight.y, SpeedT()) * _s;

        void AdvanceGait(float speed, bool moving, float dt)
        {
            _moveBlend = Mathf.MoveTowards(_moveBlend, moving ? 1f : 0f, dt * (moving ? 6f : 4f));
            if (moving)
            {
                _phase += speed * dt / CycleLength() * (Mathf.PI * 2f);
                _phase = Mathf.Repeat(_phase, Mathf.PI * 2f);
            }
        }

        /// <summary>
        /// Planted / swinging foot. The phase window for this foot opens at its offset; while
        /// inside the window the foot swings from where it was lifted to a landing predicted
        /// from the current hip and velocity. Outside the window it is locked in world space.
        /// </summary>
        void UpdateFoot(ref Foot f, int side, Vector3 hip, bool moving, float dt, float offset)
        {
            if (f.idleStepping)
            {
                return; // handled by IdleSteps
            }

            float local = Mathf.Repeat(_phase - offset, Mathf.PI * 2f);
            float swingLen = SwingFrac() * Mathf.PI * 2f;
            bool inWindow = moving && local < swingLen;

            if (inWindow)
            {
                if (!f.swinging)
                {
                    f.swinging = true;
                    f.liftFrom = f.pos;
                    f.liftYaw = f.yaw;
                    f.target = PredictLanding(hip, side, 0f);
                }

                f.swingT = Mathf.Clamp01(local / swingLen);

                // Re-aim the landing while the foot is still early in its arc so turns
                // land where the body is actually going.
                if (f.swingT < 0.55f)
                {
                    Vector3 fresh = PredictLanding(hip, side, f.swingT);
                    f.target = Vector3.Lerp(f.target, fresh, 1f - Mathf.Exp(-14f * dt));
                }

                float k = Mathf.SmoothStep(0f, 1f, f.swingT);
                float arc = Mathf.Sin(f.swingT * Mathf.PI);
                f.pos = Vector3.Lerp(f.liftFrom, f.target, k) + Vector3.up * (arc * StepHeight());
                f.yaw = Mathf.LerpAngle(f.liftYaw, transform.eulerAngles.y + side * 4f, k);
            }
            else
            {
                if (f.swinging)
                {
                    f.swinging = false;
                    f.planted = f.target;
                    f.plantedYaw = f.yaw;
                }

                if (!moving && f.swingT > 0f && f.swingT < 1f && Vector3.Distance(f.pos, f.planted) > 0.02f)
                {
                    // Stopped mid-stride: finish the step quickly instead of hanging in the air.
                    f.pos = Vector3.MoveTowards(f.pos, f.planted, 2.2f * dt);
                    f.pos.y = Mathf.Lerp(f.pos.y, f.planted.y, 1f - Mathf.Exp(-16f * dt));
                    if (Vector3.Distance(f.pos, f.planted) <= 0.02f) f.swingT = 0f;
                }
                else
                {
                    f.pos = f.planted;
                    f.yaw = f.plantedYaw;
                }
            }
        }

        Vector3 PredictLanding(Vector3 hip, int side, float swingT)
        {
            float cycle = CycleLength();
            float sf = SwingFrac();
            Vector3 dir = _vel.sqrMagnitude > 0.01f ? _vel.normalized : transform.forward;

            // Hip travels cycle*sf during the swing; the foot lands half a stance ahead.
            float ahead = cycle * sf * (1f - swingT) + cycle * (1f - sf) * 0.5f;
            // Keep the feet under the body's own right axis, not the velocity's, so strafing
            // and backpedalling don't cross the legs.
            Vector3 bodyRight = transform.right;
            Vector3 basePoint = new Vector3(hip.x, transform.position.y, hip.z) + bodyRight * (side * footSpacing * 0.35f);

            Vector3 land = basePoint + dir * ahead;
            return GroundAt(land) + Vector3.up * _ankleH;
        }

        Vector3 GroundAt(Vector3 p)
        {
            Vector3 origin = p + Vector3.up * 0.7f;
            int mask = motor != null ? motor.groundMask.value : ~0;
            var hits = Physics.RaycastAll(origin, Vector3.down, 1.4f, mask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }

            if (best > float.NegativeInfinity)
            {
                // Don't accept a surface far above the capsule base (a low ceiling / crate top).
                if (best > transform.position.y + 0.45f) best = transform.position.y;
                return new Vector3(p.x, best, p.z);
            }

            return new Vector3(p.x, transform.position.y, p.z);
        }

        /// <summary>
        /// Standing still, a foot that has drifted (turn-in-place drags the body round over
        /// planted feet) takes a quick corrective step home. One foot at a time.
        /// </summary>
        void IdleSteps(bool moving, float dt, Vector3 pos, Vector3 right, Vector3 fwd)
        {
            float yaw = transform.eulerAngles.y;
            StepIdle(ref _footL, -1, moving, dt, yaw);
            StepIdle(ref _footR, 1, moving, dt, yaw);

            if (moving || _footL.idleStepping || _footR.idleStepping)
            {
                return;
            }

            // Pick the worse foot; only start if it is genuinely out of place.
            Vector3 homeL = GroundAt(pos - right * footSpacing + fwd * 0.02f) + Vector3.up * _ankleH;
            Vector3 homeR = GroundAt(pos + right * footSpacing + fwd * 0.02f) + Vector3.up * _ankleH;
            float dL = Vector3.Distance(_footL.pos, homeL) + Mathf.Abs(Mathf.DeltaAngle(_footL.yaw, yaw - 4f)) * 0.004f;
            float dR = Vector3.Distance(_footR.pos, homeR) + Mathf.Abs(Mathf.DeltaAngle(_footR.yaw, yaw + 4f)) * 0.004f;

            if (Mathf.Max(dL, dR) < idleStepThreshold)
            {
                return;
            }

            bool stepLeft = dL > dR;
            if (Mathf.Abs(dL - dR) < 0.05f) stepLeft = _lastIdleFoot == 1; // alternate on ties
            _lastIdleFoot = stepLeft ? -1 : 1;

            ref Foot f = ref (stepLeft ? ref _footL : ref _footR);
            f.idleStepping = true;
            f.idleT = 0f;
            f.liftFrom = f.pos;
            f.liftYaw = f.yaw;
            f.target = stepLeft ? homeL : homeR;
            f.swinging = false;
        }

        void StepIdle(ref Foot f, int side, bool moving, float dt, float yaw)
        {
            if (!f.idleStepping)
            {
                return;
            }

            if (moving)
            {
                // Locomotion takes over; land wherever we are.
                f.idleStepping = false;
                f.planted = f.pos;
                f.plantedYaw = f.yaw;
                return;
            }

            f.idleT += dt / Mathf.Max(0.05f, idleStepDuration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.idleT));
            f.pos = Vector3.Lerp(f.liftFrom, f.target, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.05f * _s);
            f.yaw = Mathf.LerpAngle(f.liftYaw, yaw + side * 4f, k);

            if (f.idleT >= 1f)
            {
                f.idleStepping = false;
                f.pos = f.target;
                f.planted = f.target;
                f.yaw = f.plantedYaw = yaw + side * 4f;
                f.swingT = 0f;
            }
        }

        /// <summary>If a planted foot is out of leg reach, the pelvis drops rather than the foot sliding.</summary>
        void ResolvePelvisDrop(Vector3 hipL, Vector3 hipR, float dt)
        {
            float reach = _legLen * 0.995f;
            float need = 0f;
            need = Mathf.Max(need, Vector3.Distance(hipL, _footL.pos) - reach);
            need = Mathf.Max(need, Vector3.Distance(hipR, _footR.pos) - reach);
            need = Mathf.Clamp(need, 0f, 0.14f);
            _pelvisDrop = Mathf.Lerp(_pelvisDrop, need, 1f - Mathf.Exp(-20f * dt));
        }

        void SolveLeg(Vector3 hip, Foot foot, Transform knee, Transform ankle, Transform visThigh, Transform visShin,
            Vector3 fwd, Vector3 right, int side)
        {
            Vector3 anklePos = foot.pos;
            Vector3 toHip = hip - anklePos;
            float maxLen = _legLen * 0.995f;
            if (toHip.magnitude > maxLen)
            {
                // Pelvis drop lags a frame; never let the knee pop straight.
                anklePos = hip - toHip.normalized * maxLen;
            }

            Quaternion footRot = Quaternion.Euler(0f, foot.yaw, 0f);
            Vector3 footFwd = footRot * Vector3.forward;
            // Knee points along the foot with a hint of outward splay.
            Vector3 pole = (footFwd + right * (side * 0.18f)).normalized;

            Vector3 kneePos = TwoBone(hip, anklePos, _thigh, _shin, pole);
            knee.position = kneePos;

            // Foot pitch: toe drops after lift, flattens for the landing.
            float pitch = 0f;
            if (foot.swinging)
            {
                pitch = 22f * Mathf.Sin(foot.swingT * Mathf.PI) * (1f - foot.swingT) * 1.6f;
            }

            ankle.SetPositionAndRotation(anklePos, footRot * Quaternion.Euler(pitch, 0f, 0f));

            PlaceSegment(visThigh, hip, kneePos, footFwd);
            PlaceSegment(visShin, kneePos, anklePos, footFwd);
        }

        // ---------------------------------------------------------------- arms

        Vector3 ProceduralHand(Vector3 shoulder, Quaternion chestRot, int side)
        {
            // Arms swing against the opposite leg. Use the real foot offset so it stays in
            // sync with the planted steps instead of a free-running sine.
            Foot opp = side > 0 ? _footL : _footR;
            Vector3 hipRef = side > 0 ? _hipL.position : _hipR.position;
            float half = Mathf.Max(0.05f, CycleLength() * (1f - SwingFrac()) * 0.5f);
            float swing = Mathf.Clamp(Vector3.Dot(opp.pos - hipRef, transform.forward) / half, -1f, 1f) * _moveBlend;

            float pitch = swing * armSwingDegrees;
            float outward = 9f + 4f * _moveBlend;
            Quaternion armRot = chestRot * Quaternion.Euler(pitch, 0f, -side * outward);
            float reach = _armLen * 0.9f;
            Vector3 hand = shoulder + armRot * Vector3.down * reach;
            // Slight elbow tuck brings the hand forward of the hip line.
            hand += chestRot * Vector3.forward * (0.03f * _s + Mathf.Abs(swing) * 0.02f);
            return hand;
        }

        void SolveArm(Vector3 shoulder, Vector3 hand, Quaternion chestRot, int side, Transform elbow, Transform wrist,
            Transform visUpper, Transform visFore)
        {
            Vector3 d = hand - shoulder;
            float max = _armLen * 0.995f;
            if (d.magnitude > max)
            {
                hand = shoulder + d.normalized * max;
            }

            // Elbows go back and out.
            Vector3 pole = (chestRot * new Vector3(side * 0.7f, -0.25f, -0.65f)).normalized;
            Vector3 elbowPos = TwoBone(shoulder, hand, _upperArm, _foreArm, pole);

            elbow.position = elbowPos;
            Vector3 foreDir = (hand - elbowPos).normalized;
            Vector3 palmFwd = chestRot * Vector3.forward;
            palmFwd -= Vector3.Project(palmFwd, foreDir);
            if (palmFwd.sqrMagnitude < 1e-5f) palmFwd = chestRot * Vector3.right * side;
            wrist.SetPositionAndRotation(hand, Quaternion.LookRotation(palmFwd.normalized, -foreDir));

            PlaceSegment(visUpper, shoulder, elbowPos, chestRot * Vector3.forward);
            PlaceSegment(visFore, elbowPos, hand, chestRot * Vector3.forward);
        }

        void ResolveWallHand(float dt, bool inCover)
        {
            bool want = inCover && cover.HasWallHand && cover.WallHandSide < 0 && !motor.IsAiming;
            if (want)
            {
                Vector3 target = cover.WallHandPoint;
                if (_wallHandBlend <= 0.001f)
                {
                    // First contact: start from where the hand actually is, so the reach reads.
                    _wallHandPos = _wristL.position;
                    _wallHandVel = Vector3.zero;
                }

                _wallHandPos = Vector3.SmoothDamp(_wallHandPos, target, ref _wallHandVel, 0.16f, Mathf.Infinity, dt);
            }

            _wallHandBlend = Mathf.MoveTowards(_wallHandBlend, want ? 1f : 0f, dt / (want ? 0.28f : 0.2f));
        }

        // ---------------------------------------------------------------- head

        void ApplyHead(Vector3 neckPos, Quaternion chestRot, float dt, bool inCover)
        {
            Vector3 want = chestRot * Vector3.forward;
            if (cam != null)
            {
                Vector3 look = cam.LookForward;
                // Limit how far the head turns away from the chest.
                Vector3 chestFwd = chestRot * Vector3.forward;
                float ang = Vector3.Angle(chestFwd, look);
                float limit = inCover ? 55f : 70f;
                want = ang > limit ? Vector3.Slerp(chestFwd, look, limit / ang) : look;
            }

            _headLook = Vector3.Slerp(_headLook.sqrMagnitude < 0.5f ? want : _headLook, want, 1f - Mathf.Exp(-9f * dt));
            Quaternion headRot = Quaternion.LookRotation(_headLook, chestRot * Vector3.up);
            Vector3 headPos = neckPos + headRot * Vector3.up * (0.12f * _s);

            _head.SetPositionAndRotation(headPos, headRot);
            // Hood sits over the head, base at the neck, set back so the face is inside it.
            Vector3 hoodBase = neckPos + headRot * new Vector3(0f, -0.02f * _s, -0.035f * _s);
            _visHood.SetPositionAndRotation(hoodBase, headRot);
            _visHoodLining.SetPositionAndRotation(hoodBase + headRot * Vector3.up * (0.005f * _s), headRot);
        }

        // ---------------------------------------------------------------- lean

        void ApplyLean(float dt, float speed)
        {
            Vector3 accel = motor.PlanarAcceleration;
            _smoothAccel = Vector3.Lerp(_smoothAccel, accel, 1f - Mathf.Exp(-8f * dt));
            float fwdAccel = Vector3.Dot(_smoothAccel, transform.forward);
            float speedLean = Mathf.InverseLerp(motor.jogSpeed, motor.sprintSpeed, speed) * 7f;
            float targetLean = Mathf.Clamp(fwdAccel * leanStrength + speedLean, -maxLean, maxLean);
            _lean = Mathf.Lerp(_lean, targetLean, 1f - Mathf.Exp(-10f * dt));

            float targetBank = Mathf.Clamp(motor.TurnVelocity * bankStrength * Mathf.Clamp01(speed / motor.jogSpeed), -maxBank, maxBank);
            _bank = Mathf.Lerp(_bank, targetBank, 1f - Mathf.Exp(-10f * dt));
        }

        // ---------------------------------------------------------------- cape

        void SimulateCape(Vector3 chestPos, Quaternion chestRot, Vector3 pelvisPos, Quaternion pelvisRot, float groundY, float dt)
        {
            int rows = _capeRowGap.Length;

            // Anchor row rides the back edge of the mantle.
            for (int c = 0; c < CapeCols; c++)
            {
                float x = (c - (CapeCols - 1) * 0.5f) * _capeColGap[0];
                Vector3 p = chestPos + chestRot * new Vector3(x, -0.05f * _s, -0.115f * _s);
                _capePos[c] = p;
                _capePrev[c] = p;
            }

            float sub = Mathf.Min(dt, 1f / 50f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / sub), 1, 3);
            float h = dt / steps;
            Vector3 back = pelvisRot * Vector3.back;
            float wind = Time.time * 1.7f;

            for (int s = 0; s < steps; s++)
            {
                // Verlet integrate free points.
                for (int i = CapeCols; i < _capePos.Length; i++)
                {
                    Vector3 p = _capePos[i];
                    Vector3 v = (p - _capePrev[i]) * capeDamping;
                    _capePrev[i] = p;
                    float n = Mathf.PerlinNoise(wind + i * 0.13f, wind * 0.7f) - 0.5f;
                    Vector3 force = Vector3.down * capeGravity + back * (0.6f + n * 1.2f) + Vector3.right * (n * 0.4f);
                    // Trail against body motion.
                    force -= _vel * 0.9f;
                    _capePos[i] = p + v + force * (h * h);
                }

                // Constraints.
                for (int it = 0; it < 3; it++)
                {
                    for (int r = 1; r < rows; r++)
                    {
                        for (int c = 0; c < CapeCols; c++)
                        {
                            int i = r * CapeCols + c;
                            Constrain(i, (r - 1) * CapeCols + c, _capeRowGap[r], r - 1 == 0);
                            if (c > 0)
                            {
                                Constrain(i, i - 1, _capeColGap[r], false);
                            }
                        }
                    }

                    // Body collision: keep the sheet behind the back plane and above the ground.
                    for (int i = CapeCols; i < _capePos.Length; i++)
                    {
                        Vector3 local = Quaternion.Inverse(pelvisRot) * (_capePos[i] - pelvisPos);
                        float minBack = -0.11f * _s;
                        if (local.y > -0.75f * _s && local.y < 0.6f * _s && Mathf.Abs(local.x) < 0.24f * _s && local.z > minBack)
                        {
                            local.z = minBack;
                            _capePos[i] = pelvisPos + pelvisRot * local;
                        }

                        if (_capePos[i].y < groundY + 0.03f)
                        {
                            _capePos[i].y = groundY + 0.03f;
                        }
                    }
                }
            }

            // Write mesh in rig-local space.
            int n0 = _capePos.Length;
            for (int i = 0; i < n0; i++)
            {
                Vector3 lp = _capeRoot.InverseTransformPoint(_capePos[i]);
                _capeVerts[i] = lp;
                _capeVerts[n0 + i] = lp;
            }

            _capeMesh.vertices = _capeVerts;
            _capeMesh.RecalculateNormals();
            _capeMesh.RecalculateBounds();
        }

        void Constrain(int i, int j, float rest, bool jFixed)
        {
            Vector3 d = _capePos[i] - _capePos[j];
            float len = d.magnitude;
            if (len < 1e-5f) return;
            float diff = (len - rest) / len;
            if (jFixed)
            {
                _capePos[i] -= d * diff;
            }
            else
            {
                _capePos[i] -= d * (diff * 0.5f);
                _capePos[j] += d * (diff * 0.5f);
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Two-bone IK: returns the middle joint for a chain a -> mid -> c.</summary>
        static Vector3 TwoBone(Vector3 a, Vector3 c, float l1, float l2, Vector3 pole)
        {
            Vector3 d = c - a;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, l1 + l2 - 1e-4f);
            Vector3 dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.down;

            float cosA = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

            Vector3 side = pole - Vector3.Project(pole, dir);
            if (side.sqrMagnitude < 1e-6f)
            {
                side = Vector3.Cross(dir, Vector3.right);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(dir, Vector3.forward);
            }

            side.Normalize();
            return a + (dir * Mathf.Cos(angA) + side * Mathf.Sin(angA)) * l1;
        }

        /// <summary>Orients a limb mesh (pivot at top, hanging -Y) to run from top to bottom.</summary>
        static void PlaceSegment(Transform vis, Vector3 top, Vector3 bottom, Vector3 forwardHint)
        {
            Vector3 axis = top - bottom;
            if (axis.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3 up = axis.normalized;
            Vector3 fwd = forwardHint - Vector3.Project(forwardHint, up);
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.Cross(up, Vector3.right);
            }

            vis.SetPositionAndRotation(top, Quaternion.LookRotation(fwd.normalized, up));
        }
    }
}
