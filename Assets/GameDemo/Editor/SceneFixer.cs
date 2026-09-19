#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GameDemo.Editor
{
    /// <summary>
    /// Repairs a scene saved against an older script set: clears dead MonoBehaviour slots left
    /// by deleted components, adds the current player components, rewires the camera, and lifts
    /// the lighting out of pure black.
    ///
    /// Use this when you want to keep hand-edited geometry. For a clean level, use
    /// GameDemo -> Build / Rebuild Demo Scene instead.
    /// </summary>
    public static class SceneFixer
    {
        [MenuItem("GameDemo/Fix Current Scene (keep geometry)")]
        public static void FixScene()
        {
            int removed = ClearMissingScripts();
            bool repaired = RepairPlayer();
            int lights = LiftLighting();

            EditorSceneManagerSave();

            string msg = $"Removed {removed} missing-script slot(s).\n" +
                         (repaired ? "Player components added and rewired.\n" : "No player found in scene.\n") +
                         $"Lighting adjusted ({lights} light(s) present).";
            Debug.Log("SceneFixer: " + msg);
            EditorUtility.DisplayDialog("Scene fixed", msg, "OK");
        }

        [MenuItem("GameDemo/Clear Missing Script Warnings")]
        public static void ClearMissingScriptsMenu()
        {
            int removed = ClearMissingScripts();
            EditorSceneManagerSave();
            Debug.Log($"SceneFixer: removed {removed} missing-script slot(s).");
        }

        static int ClearMissingScripts()
        {
            int total = 0;
            foreach (GameObject go in AllSceneObjects())
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count > 0)
                {
                    total += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    EditorUtility.SetDirty(go);
                }
            }

            return total;
        }

        static bool RepairPlayer()
        {
            var motor = Object.FindFirstObjectByType<TlouPlayerMotor>();
            if (motor == null)
            {
                return false;
            }

            GameObject player = motor.gameObject;

            // Legacy visuals were plain primitives parented under these names.
            foreach (string legacy in new[] { "Visual", "Rig", "FloatingHand" })
            {
                Transform t = player.transform.Find(legacy);
                if (t != null)
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            var cover = Require<TlouCover>(player);
            var traversal = Require<TlouTraversal>(player);

            bool hasImported = player.transform.Find("ImportedCharacter") != null;
            HeroAvatar avatar = player.GetComponent<HeroAvatar>();
            if (avatar == null && !hasImported)
            {
                avatar = player.AddComponent<HeroAvatar>();
            }

            var weapon = Require<PlayerWeapon>(player);

            var cam = Object.FindFirstObjectByType<TlouCamera>();
            if (cam == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    var go = new GameObject("OTS_Camera") { tag = "MainCamera" };
                    main = go.AddComponent<Camera>();
                    main.clearFlags = CameraClearFlags.SolidColor;
                    main.backgroundColor = Color.black;
                    go.transform.position = player.transform.position + new Vector3(0.55f, 1.5f, -2.3f);
                    if (Object.FindFirstObjectByType<AudioListener>() == null)
                    {
                        go.AddComponent<AudioListener>();
                    }
                }

                cam = main.gameObject.AddComponent<TlouCamera>();
            }

            // An old scene serialised a much shorter boom; reset framing to current defaults.
            cam.shoulderOffset = 0.58f;
            cam.pivotHeight = 1.46f;
            cam.distance = 2.35f;
            cam.normalFov = 64f;
            cam.minPitch = -55f;
            cam.maxPitch = 62f;

            motor.cam = cam;
            cover.motor = motor;
            cover.cam = cam;
            traversal.motor = motor;
            traversal.cover = cover;
            traversal.cam = cam;

            if (avatar != null)
            {
                avatar.motor = motor;
                avatar.cover = cover;
                avatar.cam = cam;
            }

            weapon.motor = motor;
            weapon.cover = cover;
            weapon.avatar = avatar;
            weapon.cam = cam;

            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(cam.gameObject);
            return true;
        }

        /// <summary>Gives a black scene enough light to be readable before the flashlight.</summary>
        static int LiftLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.1f, 0.115f, 0.145f);
            RenderSettings.ambientEquatorColor = new Color(0.075f, 0.08f, 0.095f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.048f);

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            int usable = 0;
            foreach (Light l in lights)
            {
                if (!l.enabled || l.intensity <= 0.05f)
                {
                    continue;
                }

                usable++;

                // Old scenes used a 0.08 intensity "almost black" fill that reads as unlit.
                if (l.type == LightType.Point && l.intensity < 0.5f)
                {
                    l.intensity = 1.1f;
                    l.range = Mathf.Max(l.range, 12f);
                    EditorUtility.SetDirty(l);
                }
            }

            if (usable == 0)
            {
                var go = new GameObject("Moonlight");
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = new Color(0.46f, 0.56f, 0.76f);
                l.intensity = 0.3f;
                l.shadows = LightShadows.Soft;
                go.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
                usable = 1;
            }

            return usable;
        }

        static T Require<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static System.Collections.Generic.List<GameObject> AllSceneObjects()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Collect(root.transform, list);
            }

            return list;
        }

        static void Collect(Transform t, System.Collections.Generic.List<GameObject> into)
        {
            into.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
            {
                Collect(t.GetChild(i), into);
            }
        }

        static void EditorSceneManagerSave()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
#endif
