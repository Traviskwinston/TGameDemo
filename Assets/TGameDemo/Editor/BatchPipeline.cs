#if UNITY_EDITOR
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GameDemo.Editor
{
    /// <summary>
    /// Headless entry point: builds the demo scene, imports the rigged character, builds its
    /// Animator, then verifies the result and prints a report. Meant for
    /// -batchmode -executeMethod GameDemo.Editor.BatchPipeline.RunAll
    /// so the whole setup can run without the editor UI.
    /// </summary>
    public static class BatchPipeline
    {
        /// <summary>Dropped in the project root to request a one-shot run on the next script reload.</summary>
        const string AutoRunFlag = "../.autorun_pipeline";

        /// <summary>Batch entry: runs everything then quits the editor.</summary>
        public static void RunAll()
        {
            Run(true);
        }

        [MenuItem("GameDemo/Run Full Setup (scene + character + animator)")]
        public static void RunFromMenu()
        {
            Run(false);
        }

        /// <summary>
        /// Honours a one-shot request left on disk. Lets the setup complete inside an already
        /// open editor as soon as it recompiles, without quitting it.
        /// </summary>
        [InitializeOnLoadMethod]
        static void MaybeAutoRun()
        {
            string flag = System.IO.Path.Combine(Application.dataPath, AutoRunFlag);
            if (!System.IO.File.Exists(flag))
            {
                return;
            }

            System.IO.File.Delete(flag);
            // Deferred so the asset import that triggered this reload has settled first.
            EditorApplication.delayCall += () => Run(false);
        }

        static void Run(bool exitAfter)
        {
            var log = new StringBuilder();
            log.AppendLine("===== GAMEDEMO PIPELINE =====");

            AssetDatabase.Refresh();

            // 1. Scene ---------------------------------------------------------
            try
            {
                DemoSceneBuilder.Build();
                log.AppendLine("scene: built and saved");
            }
            catch (System.Exception e)
            {
                log.AppendLine("scene: FAILED " + e.Message);
                Finish(log, exitAfter, 2);
                return;
            }

            // 2. Character -----------------------------------------------------
            string model = CharacterImporter.FindBestModel();
            log.AppendLine("model: " + (model ?? "NONE FOUND"));
            if (string.IsNullOrEmpty(model))
            {
                Finish(log, exitAfter, 3);
                return;
            }

            try
            {
                CharacterImporter.ImportCharacter();
            }
            catch (System.Exception e)
            {
                log.AppendLine("import: FAILED " + e);
                Finish(log, exitAfter, 4);
                return;
            }

            // 3. Verify --------------------------------------------------------
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Avatar>().FirstOrDefault();
            log.AppendLine($"avatar: exists={avatar != null} valid={(avatar != null && avatar.isValid)} human={(avatar != null && avatar.isHuman)}");

            var clips = AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToList();
            log.AppendLine($"clips: {clips.Count}");
            foreach (string want in new[] { "Idle", "Walking_A", "Running_A", "Running_B", "Walking_Backwards",
                                            "Running_Strafe_Left", "Running_Strafe_Right", "Jump_Idle",
                                            "1H_Ranged_Aiming", "1H_Ranged_Shoot", "Lie_Idle" })
            {
                AnimationClip c = clips.FirstOrDefault(x => x.name == want);
                log.AppendLine($"  {want,-22} {(c != null ? $"ok loop={c.isLooping}" : "MISSING")}");
            }

            var motor = Object.FindFirstObjectByType<TlouPlayerMotor>();
            log.AppendLine("player in scene: " + (motor != null));
            bool ok = motor != null;
            if (motor != null)
            {
                Transform imported = motor.transform.Find("ImportedCharacter");
                log.AppendLine("ImportedCharacter child: " + (imported != null));

                // A stray character at scene root means a reimport reverted the prefab
                // overrides again, which is how this failed silently the first time.
                var strays = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
                    .Where(a => a.GetComponentInParent<TlouPlayerMotor>() == null).ToList();
                log.AppendLine("orphan animators at root: " + strays.Count +
                               (strays.Count > 0 ? " (" + string.Join(", ", strays.Select(a => a.name)) + ")" : ""));
                if (imported == null || strays.Count > 0)
                {
                    ok = false;
                }

                if (imported != null)
                {
                    var anim = imported.GetComponentInChildren<Animator>();
                    log.AppendLine("animator: " + (anim != null));
                    if (anim != null)
                    {
                        log.AppendLine("  avatar assigned: " + (anim.avatar != null));
                        log.AppendLine("  isHuman: " + anim.isHuman);
                        var ctrl = anim.runtimeAnimatorController as AnimatorController;
                        log.AppendLine("  controller: " + (ctrl != null ? ctrl.name : "NONE"));
                        if (ctrl != null)
                        {
                            log.AppendLine("  layers: " + string.Join(", ", ctrl.layers.Select(l => l.name)));
                            log.AppendLine("  params: " + string.Join(", ", ctrl.parameters.Select(p => p.name)));
                            foreach (AnimatorControllerLayer l in ctrl.layers)
                            {
                                log.AppendLine($"  layer '{l.name}' states: " +
                                               string.Join(", ", l.stateMachine.states.Select(s => s.state.name)));
                            }
                        }

                        bool rig = anim.GetComponent<HumanoidRig>() != null;
                        log.AppendLine("  HumanoidRig: " + rig);
                        if (anim.avatar == null || !anim.isHuman || anim.runtimeAnimatorController == null || !rig)
                        {
                            ok = false;
                        }
                    }
                    else
                    {
                        ok = false;
                    }

                    var skinned = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    log.AppendLine($"  skinned meshes: {skinned.Length} ({string.Join(", ", skinned.Select(s => s.name))})");
                    log.AppendLine($"  scale: {imported.localScale.x:0.000}");
                }

                var hero = motor.GetComponent<HeroAvatar>();
                log.AppendLine("procedural HeroAvatar enabled: " + (hero != null && hero.enabled));

                var driver = motor.GetComponent<AnimatorDriver>();
                log.AppendLine("AnimatorDriver: " + (driver != null) +
                               (driver != null ? " animator=" + (driver.animator != null) : ""));
            }

            // Persist the scene changes the importer made.
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            log.AppendLine("scene saved: " + saved);

            AssetDatabase.SaveAssets();
            log.AppendLine(ok ? "RESULT: OK" : "RESULT: INCOMPLETE - see flags above");
            Finish(log, exitAfter, ok ? 0 : 5);
        }

        static void Finish(StringBuilder log, bool exitAfter, int code)
        {
            log.AppendLine("===== END PIPELINE =====");
            if (code == 0)
            {
                Debug.Log(log.ToString());
            }
            else
            {
                Debug.LogError(log.ToString());
            }

            // Also written beside the project so the report can be read without the editor.
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(Application.dataPath, "../pipeline_report.txt"), log.ToString());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("BatchPipeline: could not write report. " + e.Message);
            }

            if (exitAfter)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
#endif
