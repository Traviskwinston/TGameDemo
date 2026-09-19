#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GameDemo.Editor
{
    /// <summary>
    /// Builds a complete Animator Controller from the clips embedded in a character FBX.
    /// Locomotion is a 2D freeform blend on MoveX/MoveY, with jump, prone, and an upper-body
    /// aim layer. Clip lookup is by common names (KayKit, Mixamo, Quaternius conventions), so
    /// a different pack still gets most of the way there.
    /// </summary>
    public static class AnimatorBuilder
    {
        // Normalised MoveX/MoveY thresholds match AnimatorDriver (speed / sprintSpeed).
        const float WalkT = 0.27f;
        const float JogT = 0.6f;
        const float SprintT = 1f;

        static readonly string[] LoopHints =
        {
            "Idle", "Walking", "Walk", "Running", "Run", "Jog", "Sprint", "Strafe", "Aiming", "Blocking",
            "Crouch", "Sneak", "Lie_Idle", "Jump_Idle", "Spellcasting", "Shooting", "Backwards", "Backward"
        };

        [MenuItem("GameDemo/Character/Build Animator From Model Clips")]
        public static void BuildMenu()
        {
            string model = CharacterImporter.FindBestModel();
            if (string.IsNullOrEmpty(model))
            {
                EditorUtility.DisplayDialog("No model", "Put a rigged FBX in " + CharacterImporter.CharacterFolder + " first.", "OK");
                return;
            }

            AnimatorController ctrl = Build(model);
            var anim = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
                .FirstOrDefault(a => a.GetComponentInParent<TlouPlayerMotor>() != null);
            if (anim != null)
            {
                anim.runtimeAnimatorController = ctrl;
                EditorUtility.SetDirty(anim);
            }

            Debug.Log("AnimatorBuilder: built " + AssetDatabase.GetAssetPath(ctrl));
        }

        public static AnimatorController Build(string modelPath)
        {
            ConfigureClipImport(modelPath);

            Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => c.name)
                .ToDictionary(g => g.Key, g => g.First());

            string dir = Path.GetDirectoryName(modelPath)?.Replace('\\', '/') ?? CharacterImporter.CharacterFolder;
            string baseName = Path.GetFileNameWithoutExtension(modelPath);
            string ctrlPath = $"{dir}/{baseName}_Controller.controller";

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(ctrlPath);
            }

            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);

            foreach (string f in new[] { "Speed", "MoveX", "MoveY" })
                ctrl.AddParameter(f, AnimatorControllerParameterType.Float);
            foreach (string b in new[] { "Grounded", "Crouch", "Prone", "Aiming", "Sprinting", "InCover" })
                ctrl.AddParameter(b, AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Fire", AnimatorControllerParameterType.Trigger);

            // Base layer needs IK pass for HumanoidRig.
            AnimatorControllerLayer[] layers = ctrl.layers;
            layers[0].iKPass = true;
            layers[0].name = "Base";
            ctrl.layers = layers;

            BuildBaseLayer(ctrl, ctrl.layers[0].stateMachine, clips);
            BuildAimLayer(ctrl, clips, dir, baseName);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // ------------------------------------------------------------ import settings

        /// <summary>Marks locomotion/idle clips as looping and locks root motion so nothing drifts.</summary>
        static void ConfigureClipImport(string modelPath)
        {
            var imp = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (imp == null)
            {
                return;
            }

            ModelImporterClipAnimation[] clips = imp.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = imp.defaultClipAnimations;
            }

            bool changed = false;
            foreach (ModelImporterClipAnimation c in clips)
            {
                bool loop = LoopHints.Any(h => c.name.Contains(h)) && !c.name.EndsWith("_Pose");
                if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
                if (!c.lockRootRotation) { c.lockRootRotation = true; changed = true; }
                if (!c.lockRootHeightY) { c.lockRootHeightY = true; changed = true; }
                if (!c.lockRootPositionXZ) { c.lockRootPositionXZ = true; changed = true; }
                if (!c.keepOriginalOrientation) { c.keepOriginalOrientation = true; changed = true; }
                if (!c.keepOriginalPositionY) { c.keepOriginalPositionY = true; changed = true; }
                if (!c.keepOriginalPositionXZ) { c.keepOriginalPositionXZ = true; changed = true; }
            }

            if (changed || imp.clipAnimations == null || imp.clipAnimations.Length == 0)
            {
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------ base layer

        static void BuildBaseLayer(AnimatorController ctrl, AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips)
        {
            AnimationClip idle = Pick(clips, "Idle", "Unarmed_Idle", "idle", "Idle_A");
            AnimationClip walk = Pick(clips, "Walking_A", "Walking", "Walk", "walk", "Walking_B");
            AnimationClip jog = Pick(clips, "Running_A", "Jog", "Run", "Running", "run");
            AnimationClip sprint = Pick(clips, "Running_B", "Sprint", "Running_A", "Run");
            AnimationClip back = Pick(clips, "Walking_Backwards", "Walk_Backward", "Walking_Backward", "Run_Backward");
            AnimationClip strafeL = Pick(clips, "Running_Strafe_Left", "Strafe_Left", "Walk_Left", "Left_Strafe");
            AnimationClip strafeR = Pick(clips, "Running_Strafe_Right", "Strafe_Right", "Walk_Right", "Right_Strafe");

            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = "MoveX",
                blendParameterY = "MoveY",
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, ctrl);

            var children = new List<ChildMotion>();
            void Add(AnimationClip clip, float x, float y, float timeScale = 1f)
            {
                if (clip == null) return;
                children.Add(new ChildMotion { motion = clip, position = new Vector2(x, y), timeScale = timeScale });
            }

            Add(idle, 0f, 0f);
            Add(walk, 0f, WalkT);
            Add(jog, 0f, JogT);
            Add(sprint ?? jog, 0f, SprintT, sprint != null ? 1f : 1.25f);
            Add(back ?? walk, 0f, -WalkT, back != null ? 1f : -1f);
            Add(back ?? walk, 0f, -JogT, back != null ? 1.5f : -1.5f);
            Add(strafeL ?? walk, -WalkT, 0f, 0.6f);
            Add(strafeL ?? walk, -JogT, 0f, 1f);
            Add(strafeR ?? walk, WalkT, 0f, 0.6f);
            Add(strafeR ?? walk, JogT, 0f, 1f);
            // Diagonals borrow forward/back clips so freeform blending has anchors there.
            Add(jog, -JogT * 0.7f, JogT * 0.7f);
            Add(jog, JogT * 0.7f, JogT * 0.7f);
            Add(back ?? walk, -WalkT * 0.7f, -WalkT * 0.7f, back != null ? 1f : -1f);
            Add(back ?? walk, WalkT * 0.7f, -WalkT * 0.7f, back != null ? 1f : -1f);
            tree.children = children.ToArray();

            AnimatorState loco = sm.AddState("Locomotion");
            loco.motion = tree;
            sm.defaultState = loco;

            // ---- jump -----------------------------------------------------------
            AnimationClip jumpStart = Pick(clips, "Jump_Start", "Jump", "JumpStart");
            AnimationClip jumpIdle = Pick(clips, "Jump_Idle", "Falling", "Fall", "JumpIdle");
            AnimationClip jumpLand = Pick(clips, "Jump_Land", "Land", "JumpLand");
            if (jumpIdle != null)
            {
                AnimatorState air = sm.AddState("Airborne"); air.motion = jumpIdle;
                AnimatorState start = jumpStart != null ? sm.AddState("JumpStart") : null;
                AnimatorState land = jumpLand != null ? sm.AddState("JumpLand") : null;
                if (start != null) start.motion = jumpStart;
                if (land != null) land.motion = jumpLand;

                AnimatorStateTransition toAir = loco.AddTransition(start ?? air);
                toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                toAir.hasExitTime = false; toAir.duration = 0.08f;

                if (start != null)
                {
                    var t = start.AddTransition(air); t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.1f;
                }

                AnimatorStateTransition toLand = air.AddTransition(land ?? loco);
                toLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                toLand.hasExitTime = false; toLand.duration = 0.05f;

                if (land != null)
                {
                    var t = land.AddTransition(loco); t.hasExitTime = true; t.exitTime = 0.6f; t.duration = 0.15f;
                    var bail = land.AddTransition(loco); bail.AddCondition(AnimatorConditionMode.Greater, 0.2f, "Speed");
                    bail.hasExitTime = false; bail.duration = 0.1f;
                }
            }

            // ---- prone ----------------------------------------------------------
            AnimationClip lieDown = Pick(clips, "Lie_Down", "Crouch_To_Prone", "ProneDown");
            AnimationClip lieIdle = Pick(clips, "Lie_Idle", "Prone_Idle", "Crawl_Idle", "Lie_Pose");
            AnimationClip lieUp = Pick(clips, "Lie_StandUp", "Prone_To_Stand", "ProneUp");
            if (lieIdle != null)
            {
                AnimatorState proneIdle = sm.AddState("Prone"); proneIdle.motion = lieIdle;
                AnimatorState down = lieDown != null ? sm.AddState("ProneDown") : null;
                AnimatorState up = lieUp != null ? sm.AddState("ProneUp") : null;
                if (down != null) down.motion = lieDown;
                if (up != null) up.motion = lieUp;

                var toDown = loco.AddTransition(down ?? proneIdle);
                toDown.AddCondition(AnimatorConditionMode.If, 0f, "Prone");
                toDown.hasExitTime = false; toDown.duration = 0.15f;
                if (down != null)
                {
                    var t = down.AddTransition(proneIdle); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.1f;
                }

                var toUp = proneIdle.AddTransition(up ?? loco);
                toUp.AddCondition(AnimatorConditionMode.IfNot, 0f, "Prone");
                toUp.hasExitTime = false; toUp.duration = 0.1f;
                if (up != null)
                {
                    var t = up.AddTransition(loco); t.hasExitTime = true; t.exitTime = 0.85f; t.duration = 0.15f;
                }
            }
        }

        // ------------------------------------------------------------ aim layer

        static void BuildAimLayer(AnimatorController ctrl, Dictionary<string, AnimationClip> clips, string dir, string baseName)
        {
            AnimationClip aim = Pick(clips, "1H_Ranged_Aiming", "Pistol_Aim", "Aim", "Aiming", "2H_Ranged_Aiming");
            AnimationClip shoot = Pick(clips, "1H_Ranged_Shoot", "Pistol_Fire", "Shoot", "Fire", "2H_Ranged_Shoot");
            if (aim == null)
            {
                return;
            }

            string maskPath = $"{dir}/{baseName}_UpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                {
                    var part = (AvatarMaskBodyPart)i;
                    bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                                 || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                                 || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                                 || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                    mask.SetHumanoidBodyPartActive(part, upper);
                }

                AssetDatabase.CreateAsset(mask, maskPath);
            }

            var sm = new AnimatorStateMachine { name = "Aim", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, ctrl);

            var layer = new AnimatorControllerLayer
            {
                name = "Aim",
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 1f,
                stateMachine = sm
            };
            ctrl.AddLayer(layer);

            AnimatorState empty = sm.AddState("Empty");
            AnimatorState aimState = sm.AddState("Aim"); aimState.motion = aim;
            sm.defaultState = empty;

            var toAim = empty.AddTransition(aimState);
            toAim.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
            toAim.hasExitTime = false; toAim.duration = 0.14f;

            var toEmpty = aimState.AddTransition(empty);
            toEmpty.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
            toEmpty.hasExitTime = false; toEmpty.duration = 0.18f;

            if (shoot != null)
            {
                AnimatorState fire = sm.AddState("Fire"); fire.motion = shoot;
                var t = aimState.AddTransition(fire);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Fire");
                t.hasExitTime = false; t.duration = 0.03f;
                var back = fire.AddTransition(aimState); back.hasExitTime = true; back.exitTime = 0.8f; back.duration = 0.1f;
            }
        }

        // ------------------------------------------------------------ helpers

        static AnimationClip Pick(Dictionary<string, AnimationClip> clips, params string[] names)
        {
            foreach (string n in names)
            {
                if (clips.TryGetValue(n, out AnimationClip c)) return c;
            }

            // Loose match: case-insensitive contains.
            foreach (string n in names)
            {
                var hit = clips.FirstOrDefault(kv => kv.Key.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit.Value != null) return hit.Value;
            }

            return null;
        }
    }
}
#endif
