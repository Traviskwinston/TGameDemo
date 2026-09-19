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
    /// Swaps the procedural hero for a downloaded rigged character.
    ///
    /// Drop any humanoid FBX/GLB into Assets/GameDemo/Characters, then run the menu item.
    /// It forces Humanoid rig import, parents the model under Player, disables the procedural
    /// avatar, and attaches AnimatorDriver so the existing motor drives it.
    /// </summary>
    public static class CharacterImporter
    {
        public const string CharacterFolder = "Assets/GameDemo/Characters";

        /// <summary>Best full-body model in the characters folder, or null.</summary>
        public static string FindBestModel()
        {
            if (!AssetDatabase.IsValidFolder(CharacterFolder))
            {
                return null;
            }

            List<string> models = FindModels();
            if (models.Count == 0)
            {
                return null;
            }

            return models.OrderByDescending(ScoreCandidate).First();
        }

        [MenuItem("GameDemo/Character/Import Rigged Character")]
        public static void ImportCharacter()
        {
            if (!AssetDatabase.IsValidFolder(CharacterFolder))
            {
                AssetDatabase.CreateFolder("Assets/GameDemo", "Characters");
                EditorUtility.DisplayDialog("No characters yet",
                    "Created " + CharacterFolder + ".\n\nPut a rigged humanoid FBX in there, then run this again.",
                    "OK");
                return;
            }

            List<string> models = FindModels();
            if (models.Count == 0)
            {
                EditorUtility.DisplayDialog("No model found",
                    "Put a rigged humanoid FBX (or GLB) in " + CharacterFolder + " and run this again.",
                    "OK");
                return;
            }

            string chosen = models[0];
            if (models.Count > 1)
            {
                // Prefer something that looks like a full body over an accessory.
                chosen = models.OrderByDescending(ScoreCandidate).First();
            }

            if (!ConfigureAsHumanoid(chosen))
            {
                return;
            }

            // Every asset reimport must happen before anything reaches the scene. Reimporting
            // a model reverts the prefab instance, which silently undoes the parenting, rename,
            // scale and prop hiding while leaving later edits in place.
            AnimatorController controller = AnimatorBuilder.Build(chosen);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(chosen);
            if (prefab == null)
            {
                Debug.LogError("CharacterImporter: could not load " + chosen);
                return;
            }

            var motor = Object.FindFirstObjectByType<TlouPlayerMotor>();
            if (motor == null)
            {
                EditorUtility.DisplayDialog("No player in scene",
                    "Open the demo scene (or build it via GameDemo -> Build / Rebuild Demo Scene) first.",
                    "OK");
                return;
            }

            AttachToPlayer(motor, prefab, chosen, controller);
        }

        static List<string> FindModels()
        {
            return AssetDatabase.FindAssets("t:Model", new[] { CharacterFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();
        }

        static int ScoreCandidate(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            int score = 0;
            if (n.Contains("rogue") || n.Contains("hood") || n.Contains("mage") || n.Contains("thief")) score += 40;
            if (n.Contains("character") || n.Contains("base") || n.Contains("body")) score += 20;
            if (n.Contains("male") || n.Contains("female") || n.Contains("human")) score += 10;
            if (n.Contains("anim") || n.Contains("outfit") || n.Contains("weapon") || n.Contains("prop")) score -= 30;

            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp != null && imp.animationType == ModelImporterAnimationType.Human) score += 15;
            return score;
        }

        static bool ConfigureAsHumanoid(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                Debug.LogError("CharacterImporter: " + path + " is not a model asset.");
                return false;
            }

            bool changed = false;
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            if (!imp.importAnimation)
            {
                imp.importAnimation = true;
                changed = true;
            }

            if (changed)
            {
                imp.SaveAndReimport();
            }

            // Explicit bone map. Rigs with IK helper bones (KayKit, Rigify exports) can trip
            // the auto-mapper into picking "handIK.l" for the hand, so we name every bone.
            ApplyExplicitMapping(imp, path);

            var avatarOk = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Any(a => a.isValid);
            if (!avatarOk)
            {
                Debug.LogWarning("CharacterImporter: Humanoid avatar for " + path +
                                 " is not valid yet. Open the model's Rig tab and click Configure to map the bones.");
            }

            return true;
        }

        static readonly Dictionary<string, string[]> BoneCandidates = new Dictionary<string, string[]>
        {
            { "Hips", new[] { "hips", "pelvis", "mixamorig:Hips", "Hips" } },
            { "Spine", new[] { "spine", "spine_01", "spine1", "mixamorig:Spine", "Spine" } },
            { "Chest", new[] { "chest", "spine_02", "spine2", "mixamorig:Spine1", "Chest" } },
            { "UpperChest", new[] { "upperchest", "upper_chest", "spine_03", "spine3", "mixamorig:Spine2" } },
            { "Neck", new[] { "neck", "mixamorig:Neck", "Neck" } },
            { "Head", new[] { "head", "mixamorig:Head", "Head" } },
            { "LeftShoulder", new[] { "shoulder.l", "clavicle.l", "clavicle_l", "LeftShoulder", "mixamorig:LeftShoulder" } },
            { "LeftUpperArm", new[] { "upperarm.l", "upper_arm.l", "upperarm_l", "LeftArm", "mixamorig:LeftArm" } },
            { "LeftLowerArm", new[] { "lowerarm.l", "forearm.l", "lowerarm_l", "LeftForeArm", "mixamorig:LeftForeArm" } },
            { "LeftHand", new[] { "wrist.l", "hand.l", "hand_l", "LeftHand", "mixamorig:LeftHand" } },
            { "LeftUpperLeg", new[] { "upperleg.l", "thigh.l", "thigh_l", "LeftUpLeg", "mixamorig:LeftUpLeg" } },
            { "LeftLowerLeg", new[] { "lowerleg.l", "shin.l", "calf_l", "LeftLeg", "mixamorig:LeftLeg" } },
            { "LeftFoot", new[] { "foot.l", "foot_l", "LeftFoot", "mixamorig:LeftFoot" } },
            { "LeftToes", new[] { "toes.l", "toe.l", "ball_l", "LeftToeBase", "mixamorig:LeftToeBase" } },
            { "RightShoulder", new[] { "shoulder.r", "clavicle.r", "clavicle_r", "RightShoulder", "mixamorig:RightShoulder" } },
            { "RightUpperArm", new[] { "upperarm.r", "upper_arm.r", "upperarm_r", "RightArm", "mixamorig:RightArm" } },
            { "RightLowerArm", new[] { "lowerarm.r", "forearm.r", "lowerarm_r", "RightForeArm", "mixamorig:RightForeArm" } },
            { "RightHand", new[] { "wrist.r", "hand.r", "hand_r", "RightHand", "mixamorig:RightHand" } },
            { "RightUpperLeg", new[] { "upperleg.r", "thigh.r", "thigh_r", "RightUpLeg", "mixamorig:RightUpLeg" } },
            { "RightLowerLeg", new[] { "lowerleg.r", "shin.r", "calf_r", "RightLeg", "mixamorig:RightLeg" } },
            { "RightFoot", new[] { "foot.r", "foot_r", "RightFoot", "mixamorig:RightFoot" } },
            { "RightToes", new[] { "toes.r", "toe.r", "ball_r", "RightToeBase", "mixamorig:RightToeBase" } },
        };

        static void ApplyExplicitMapping(ModelImporter imp, string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                return;
            }

            // Actual transform names, case-preserved, for exact assignment.
            var names = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!names.ContainsKey(t.name))
                {
                    names[t.name] = t.name;
                }
            }

            var human = new List<HumanBone>();
            foreach (KeyValuePair<string, string[]> kv in BoneCandidates)
            {
                foreach (string cand in kv.Value)
                {
                    if (names.TryGetValue(cand, out string actual))
                    {
                        human.Add(new HumanBone
                        {
                            humanName = kv.Key,
                            boneName = actual,
                            limit = new HumanLimit { useDefaultValues = true }
                        });
                        break;
                    }
                }
            }

            // The 15 bones Unity requires; bail to the auto-mapper if we can't name them all.
            string[] required =
            {
                "Hips", "Spine", "Head", "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg",
                "RightFoot", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand"
            };
            if (required.Any(r => human.All(h => h.humanName != r)))
            {
                Debug.Log("CharacterImporter: rig names not recognised, leaving Unity's auto-map in place.");
                return;
            }

            HumanDescription desc = imp.humanDescription;
            desc.human = human.ToArray();
            imp.humanDescription = desc;
            imp.SaveAndReimport();
            Debug.Log($"CharacterImporter: mapped {human.Count} humanoid bones explicitly.");
        }

        static void AttachToPlayer(TlouPlayerMotor motor, GameObject prefab, string path, AnimatorController controller)
        {
            foreach (string stale in new[] { "ImportedCharacter", Path.GetFileNameWithoutExtension(path) })
            {
                Transform existing = motor.transform.Find(stale);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // Unpacked so the hierarchy is concrete scene objects. A prefab instance can have
            // all of these overrides reverted by any later reimport of the source model.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            instance.name = "ImportedCharacter";
            instance.transform.SetParent(motor.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            ScaleToCapsule(motor, instance);

            // Packs ship every weapon parented to the hand slots. Body parts are skinned;
            // props are plain MeshRenderers, so hide those and let PlayerWeapon supply the gun.
            int hidden = 0;
            foreach (MeshRenderer mr in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.gameObject.SetActive(false);
                hidden++;
            }

            if (hidden > 0)
            {
                Debug.Log($"CharacterImporter: hid {hidden} prop mesh(es) bundled in the model.");
            }

            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var driver = motor.GetComponent<AnimatorDriver>();
            if (driver == null)
            {
                driver = motor.gameObject.AddComponent<AnimatorDriver>();
            }

            driver.motor = motor;
            driver.cover = motor.GetComponent<TlouCover>();
            driver.animator = animator;

            // Procedural hero steps aside. The weapon keeps posing itself from the camera;
            // HumanoidRig puts the imported hands on it.
            var hero = motor.GetComponent<HeroAvatar>();
            if (hero != null)
            {
                hero.enabled = false;
                foreach (string n in new[] { "HeroRig", "Avatar" })
                {
                    Transform built = motor.transform.Find(n);
                    if (built != null)
                    {
                        built.gameObject.SetActive(false);
                    }
                }
            }

            var weapon = motor.GetComponent<PlayerWeapon>();
            if (weapon != null)
            {
                weapon.avatar = null;
            }

            var rig = animator.GetComponent<HumanoidRig>();
            if (rig == null)
            {
                rig = animator.gameObject.AddComponent<HumanoidRig>();
            }

            rig.motor = motor;
            rig.weapon = weapon;
            rig.cover = motor.GetComponent<TlouCover>();
            rig.cam = Object.FindFirstObjectByType<TlouCamera>();

            animator.runtimeAnimatorController = controller;

            EditorUtility.SetDirty(motor.gameObject);
            EditorUtility.SetDirty(animator);
            Debug.Log("CharacterImporter: attached " + Path.GetFileName(path) + " with controller " +
                      (controller != null ? AssetDatabase.GetAssetPath(controller) : "NONE"));
        }

        /// <summary>Rescales the model so its head lands at the controller's capsule height.</summary>
        static void ScaleToCapsule(TlouPlayerMotor motor, GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }

            float modelHeight = b.size.y;
            if (modelHeight < 0.05f)
            {
                return;
            }

            float wanted = motor.standingHeight;
            float factor = wanted / modelHeight;
            if (Mathf.Abs(factor - 1f) > 0.04f)
            {
                instance.transform.localScale *= factor;
                Debug.Log($"CharacterImporter: scaled model by {factor:0.00} to match a {wanted:0.00}m capsule.");
            }
        }

        [MenuItem("GameDemo/Character/Revert To Procedural Hero")]
        public static void RevertToProcedural()
        {
            var motor = Object.FindFirstObjectByType<TlouPlayerMotor>();
            if (motor == null)
            {
                return;
            }

            Transform imported = motor.transform.Find("ImportedCharacter");
            if (imported != null)
            {
                Object.DestroyImmediate(imported.gameObject);
            }

            var driver = motor.GetComponent<AnimatorDriver>();
            if (driver != null)
            {
                Object.DestroyImmediate(driver);
            }

            var hero = motor.GetComponent<HeroAvatar>();
            if (hero == null)
            {
                hero = motor.gameObject.AddComponent<HeroAvatar>();
            }

            hero.enabled = true;
            hero.motor = motor;
            hero.cover = motor.GetComponent<TlouCover>();
            hero.cam = Object.FindFirstObjectByType<TlouCamera>();

            Transform built = motor.transform.Find("Avatar");
            if (built != null)
            {
                built.gameObject.SetActive(true);
            }

            var weapon = motor.GetComponent<PlayerWeapon>();
            if (weapon != null)
            {
                weapon.avatar = hero;
            }

            Debug.Log("CharacterImporter: reverted to the procedural hero.");
        }
    }
}
#endif
