#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameDemo.Editor
{
    /// <summary>
    /// Builds the movement testbed: a dark keep interior with corners to lean around,
    /// stairs and a ramp to prove step smoothing, low cover to crouch behind, and open
    /// floor long enough to feel a sprint turn arc.
    ///
    /// Menu: GameDemo -> Build / Rebuild Demo Scene
    /// </summary>
    public static class DemoSceneBuilder
    {
        const string ScenePath = "Assets/TGameDemo/Scenes/DemoRoom.unity";

        static Material _stone, _floor, _wood, _mossStone, _accent, _metal;

        [MenuItem("GameDemo/Build / Rebuild Demo Scene")]
        public static void Build()
        {
            EnsureFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            SetupLighting();
            MakeMaterials();

            BuildShell();
            BuildCornerMaze();
            BuildPillars();
            BuildStairsAndRamp();
            BuildLowCover();
            BuildTallCover();
            BuildCrawlBox(new Vector3(7.5f, 0f, 5.2f));
            BuildBraziers();

            GameObject player = BuildPlayer();
            player.transform.position = new Vector3(0f, 0.1f, -6.5f);

            GameObject camGo = BuildCamera(player);
            WirePlayer(player, camGo);

            BuildHintCanvas();

            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScene();

            Debug.Log("GameDemo scene rebuilt -> " + ScenePath);
        }

        // ---------------------------------------------------------------- lighting

        static void SetupLighting()
        {
            // Dark, but never unreadable: ambient alone must show the room's shape so a
            // missing flashlight looks like a dim room rather than a broken build.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.115f, 0.13f, 0.165f);
            RenderSettings.ambientEquatorColor = new Color(0.085f, 0.09f, 0.108f);
            RenderSettings.ambientGroundColor = new Color(0.045f, 0.045f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.03f, 0.036f, 0.05f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.reflectionIntensity = 0.15f;

            // The room is roofed, so this mostly rakes the walls through the openings.
            var moon = new GameObject("Moonlight");
            var l = moon.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.46f, 0.57f, 0.78f);
            l.intensity = 0.34f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.7f;
            moon.transform.rotation = Quaternion.Euler(58f, 31f, 0f);
        }

        static void MakeMaterials()
        {
            _stone = MakeMat("Demo_Stone", new Color(0.2f, 0.205f, 0.22f), 0.02f, 0.18f);
            _floor = MakeMat("Demo_Floor", new Color(0.145f, 0.14f, 0.135f), 0.03f, 0.22f);
            _wood = MakeMat("Demo_Wood", new Color(0.24f, 0.17f, 0.11f), 0.02f, 0.2f);
            _mossStone = MakeMat("Demo_MossStone", new Color(0.16f, 0.2f, 0.16f), 0.02f, 0.25f);
            _accent = MakeMat("Demo_Accent", new Color(0.11f, 0.35f, 0.33f), 0.1f, 0.5f);
            _metal = MakeMat("Demo_Metal", new Color(0.26f, 0.26f, 0.28f), 0.6f, 0.45f);
        }

        // ---------------------------------------------------------------- geometry

        const float RoomW = 26f;
        const float RoomD = 20f;
        const float RoomH = 4.6f;

        static void BuildShell()
        {
            var root = Group("Shell");
            Box(root, "Floor", new Vector3(0f, -0.15f, 0f), new Vector3(RoomW, 0.3f, RoomD), _floor);
            Box(root, "Ceiling", new Vector3(0f, RoomH + 0.15f, 0f), new Vector3(RoomW, 0.3f, RoomD), _stone);

            Wall(root, "Wall_N", new Vector3(0f, RoomH * 0.5f, RoomD * 0.5f), new Vector3(RoomW, RoomH, 0.4f));
            Wall(root, "Wall_S", new Vector3(0f, RoomH * 0.5f, -RoomD * 0.5f), new Vector3(RoomW, RoomH, 0.4f));
            Wall(root, "Wall_E", new Vector3(RoomW * 0.5f, RoomH * 0.5f, 0f), new Vector3(0.4f, RoomH, RoomD));
            Wall(root, "Wall_W", new Vector3(-RoomW * 0.5f, RoomH * 0.5f, 0f), new Vector3(0.4f, RoomH, RoomD));
        }

        /// <summary>
        /// Interlocking L walls. Every inside corner here is a valid lean point, and the long
        /// straights between them must NOT grab the player.
        /// </summary>
        static void BuildCornerMaze()
        {
            var root = Group("CornerMaze");
            float h = 2.9f;
            float t = 0.5f;

            // Left L: long run west, short return north. One inside corner.
            Wall(root, "L1_Long", new Vector3(-6.5f, h * 0.5f, -1.5f), new Vector3(9f, h, t));
            Wall(root, "L1_Return", new Vector3(-2.25f, h * 0.5f, 1.0f), new Vector3(t, h, 5f));

            // Facing L, offset to leave a 2.4m gap you can slip through.
            Wall(root, "L2_Long", new Vector3(-7.5f, h * 0.5f, 4.5f), new Vector3(7f, h, t));
            Wall(root, "L2_Return", new Vector3(-10.75f, h * 0.5f, 2.0f), new Vector3(t, h, 5f));

            // Free-standing stub: two corners close together, good for corner-to-corner moves.
            Wall(root, "Stub_A", new Vector3(1.5f, h * 0.5f, 3.2f), new Vector3(0.5f, h, 4.5f));
            Wall(root, "Stub_B", new Vector3(3.6f, h * 0.5f, 5.2f), new Vector3(4.7f, h, 0.5f));

            // Doorway frame: a corner on each jamb.
            Wall(root, "Door_L", new Vector3(-1.6f, h * 0.5f, -6.5f), new Vector3(5f, h, t));
            Wall(root, "Door_R", new Vector3(5.4f, h * 0.5f, -6.5f), new Vector3(5f, h, t));
        }

        static void BuildPillars()
        {
            var root = Group("Pillars");
            float h = RoomH;
            Vector3[] spots =
            {
                new Vector3(9.5f, 0f, -2.5f),
                new Vector3(9.5f, 0f, 1.5f),
                new Vector3(-9.5f, 0f, -6.0f),
                new Vector3(6.0f, 0f, -2.0f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 p = spots[i] + Vector3.up * (h * 0.5f);
                Wall(root, "Pillar_" + i, p, new Vector3(0.95f, h, 0.95f));
                Box(root, "PillarBase_" + i, spots[i] + Vector3.up * 0.09f,
                    new Vector3(1.25f, 0.18f, 1.25f), _mossStone);
            }
        }

        static void BuildStairsAndRamp()
        {
            var root = Group("Traversal");

            // Stairs: deliberately small risers so step-pop smoothing is visible if it breaks.
            int steps = 7;
            float rise = 0.17f;
            float run = 0.34f;
            for (int i = 0; i < steps; i++)
            {
                Box(root, "Step_" + i,
                    new Vector3(-11.4f, rise * (i + 0.5f), -9.0f + run * i),
                    new Vector3(2.6f, rise, run), _stone);
            }

            float top = rise * steps;
            Box(root, "Landing", new Vector3(-11.4f, top - 0.09f, -6.4f), new Vector3(2.6f, 0.18f, 2.6f), _stone);

            // Ramp for slope handling.
            var ramp = Box(root, "Ramp", new Vector3(11.0f, 0.62f, -7.4f), new Vector3(3.2f, 0.25f, 5.6f), _wood);
            ramp.transform.rotation = Quaternion.Euler(-13f, 0f, 0f);
        }

        static void BuildLowCover()
        {
            var root = Group("LowCover");

            // Crates: crouch-only cling.
            AddCover(Box(root, "Crate_A", new Vector3(3.2f, 0.45f, -3.4f), new Vector3(1.5f, 0.9f, 1.1f), _wood),
                CoverSurface.CoverKind.Mid, vault: true, climb: false, peekOver: true);
            AddCover(Box(root, "Crate_B", new Vector3(-4.6f, 0.45f, 2.6f), new Vector3(2.2f, 0.9f, 1.0f), _wood),
                CoverSurface.CoverKind.Mid, vault: true, climb: false, peekOver: true);

            // Long low wall: slide along it, peek over the lip.
            AddCover(Box(root, "LowWall", new Vector3(7.0f, 0.5f, 0.6f), new Vector3(6.0f, 1.0f, 0.55f), _mossStone),
                CoverSurface.CoverKind.Mid, vault: true, climb: false, peekOver: true);
        }

        static void BuildTallCover()
        {
            var root = Group("TallCover");

            // Tall block: climb onto it.
            AddCover(Box(root, "TallBlock", new Vector3(-7.0f, 1.05f, 7.5f), new Vector3(2.2f, 2.1f, 1.8f), _stone),
                CoverSurface.CoverKind.Tall, vault: false, climb: true, peekOver: false);
        }

        static void BuildCrawlBox(Vector3 center)
        {
            var root = Group("CrawlBox");
            float w = 3.2f, d = 3.6f, h = 2.3f;
            float tunnelH = 0.8f;
            float tunnelW = 1.0f;

            AddCover(Box(root, "Crawl_L", center + new Vector3(-w * 0.5f + 0.2f, h * 0.5f, 0f), new Vector3(0.4f, h, d), _wood),
                CoverSurface.CoverKind.Tall, false, false, false);
            AddCover(Box(root, "Crawl_R", center + new Vector3(w * 0.5f - 0.2f, h * 0.5f, 0f), new Vector3(0.4f, h, d), _wood),
                CoverSurface.CoverKind.Tall, false, false, false);

            Box(root, "Crawl_Top", center + new Vector3(0f, h - 0.15f, 0f), new Vector3(w - 0.55f, 0.3f, d), _wood);

            float headerH = h - tunnelH - 0.15f;
            Box(root, "Crawl_HeaderS", center + new Vector3(0f, tunnelH + headerH * 0.5f, -d * 0.5f + 0.2f),
                new Vector3(tunnelW + 0.6f, headerH, 0.4f), _wood);
            Box(root, "Crawl_HeaderN", center + new Vector3(0f, tunnelH + headerH * 0.5f, d * 0.5f - 0.2f),
                new Vector3(tunnelW + 0.6f, headerH, 0.4f), _wood);

            float sideW = (w - tunnelW) * 0.5f - 0.2f;
            Box(root, "Crawl_FillL", center + new Vector3(-(tunnelW * 0.5f + sideW * 0.5f), h * 0.5f, 0f),
                new Vector3(sideW, h, d - 0.5f), _wood);
            Box(root, "Crawl_FillR", center + new Vector3(tunnelW * 0.5f + sideW * 0.5f, h * 0.5f, 0f),
                new Vector3(sideW, h, d - 0.5f), _wood);

            Box(root, "Crawl_Rim", center + new Vector3(0f, tunnelH + 0.04f, 0f),
                new Vector3(tunnelW + 0.12f, 0.06f, d - 0.3f), _accent);

            var trigger = new GameObject("CrawlSpace_Trigger");
            trigger.transform.SetParent(root, false);
            trigger.transform.position = center + new Vector3(0f, tunnelH * 0.5f, 0f);
            var bc = trigger.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(tunnelW * 0.9f, tunnelH, d * 0.85f);
            trigger.AddComponent<CrawlSpace>();
        }

        /// <summary>Warm pools of light so the dark has contrast to read against.</summary>
        static void BuildBraziers()
        {
            var root = Group("Braziers");
            Vector3[] spots =
            {
                new Vector3(-11.8f, 2.3f, 8.4f),
                new Vector3(11.8f, 2.3f, 8.4f),
                new Vector3(0f, 2.5f, 9.3f),
                new Vector3(-12.2f, 2.3f, -5.0f)
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject("Brazier_" + i);
                go.transform.SetParent(root, false);
                go.transform.position = spots[i];

                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 13f;
                l.intensity = 2.1f;
                l.color = new Color(1f, 0.62f, 0.32f);
                l.shadows = LightShadows.Soft;
                l.shadowStrength = 0.6f;
                // Forward rendering can demote extra lights to vertex lighting, which reads
                // as unlit on large flat walls.
                l.renderMode = LightRenderMode.ForcePixel;

                Box(go.transform, "Bowl", spots[i] + Vector3.down * 0.12f,
                    new Vector3(0.36f, 0.16f, 0.36f), _metal);
            }
        }

        // ---------------------------------------------------------------- player

        static GameObject BuildPlayer()
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.72f;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, 0.86f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.025f;

            root.AddComponent<TlouPlayerMotor>();
            root.AddComponent<TlouCover>();
            root.AddComponent<TlouTraversal>();
            root.AddComponent<HeroAvatar>();
            root.AddComponent<PlayerWeapon>();
            return root;
        }

        static GameObject BuildCamera(GameObject player)
        {
            var go = new GameObject("OTS_Camera");
            go.tag = "MainCamera";
            var c = go.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            c.nearClipPlane = 0.04f;
            c.farClipPlane = 120f;
            c.allowMSAA = true;
            go.AddComponent<TlouCamera>();
            go.AddComponent<AudioListener>();
            go.transform.position = player.transform.position + new Vector3(0.55f, 1.5f, -2.3f);
            return go;
        }

        static void WirePlayer(GameObject player, GameObject camGo)
        {
            var motor = player.GetComponent<TlouPlayerMotor>();
            var cover = player.GetComponent<TlouCover>();
            var trav = player.GetComponent<TlouTraversal>();
            var avatar = player.GetComponent<HeroAvatar>();
            var weapon = player.GetComponent<PlayerWeapon>();
            var otsCam = camGo.GetComponent<TlouCamera>();

            motor.cam = otsCam;

            cover.motor = motor;
            cover.cam = otsCam;

            trav.motor = motor;
            trav.cover = cover;
            trav.cam = otsCam;

            avatar.motor = motor;
            avatar.cover = cover;
            avatar.cam = otsCam;

            weapon.motor = motor;
            weapon.cover = cover;
            weapon.avatar = avatar;
            weapon.cam = otsCam;

            otsCam.Bind(motor, cover, trav);
        }

        // ---------------------------------------------------------------- helpers

        static Transform Group(string name)
        {
            return new GameObject(name).transform;
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
            return go;
        }

        /// <summary>Wall = box that is also tall cover, so corner leaning works on it.</summary>
        static GameObject Wall(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            GameObject go = Box(parent, name, pos, scale, _stone);
            AddCover(go, CoverSurface.CoverKind.Tall, vault: false, climb: false, peekOver: false);
            return go;
        }

        static void AddCover(GameObject go, CoverSurface.CoverKind kind, bool vault, bool climb, bool peekOver)
        {
            var cs = go.GetComponent<CoverSurface>();
            if (cs == null)
            {
                cs = go.AddComponent<CoverSurface>();
            }

            cs.kind = kind;
            cs.allowVault = vault;
            cs.allowLedgeClimb = climb;
            cs.allowPeekOver = peekOver;
        }

        static Material MakeMat(string name, Color color, float metallic, float smoothness)
        {
            string path = "Assets/TGameDemo/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material mat = existing;

            if (mat == null)
            {
                Shader shader = Shader.Find("Standard")
                                ?? Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Diffuse");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = color;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void BuildHintCanvas()
        {
            var canvasGo = new GameObject("HintCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
                UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var textGo = new GameObject("Controls");
            textGo.transform.SetParent(canvasGo.transform, false);
            var text = textGo.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = "WASD  Shift sprint  Alt walk  C crouch  Z prone\n" +
                        "RMB aim  LMB fire  F flashlight / lamp / off  Space jump / vault / climb  E interact";
            text.fontSize = 15;
            text.color = new Color(0.8f, 0.82f, 0.8f, 0.6f);
            text.alignment = TextAnchor.LowerLeft;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.02f, 0.02f);
            rt.anchorMax = new Vector2(0.6f, 0.14f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void RegisterScene()
        {
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.path == ScenePath)
                {
                    return;
                }
            }

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/TGameDemo")) AssetDatabase.CreateFolder("Assets", "GameDemo");
            if (!AssetDatabase.IsValidFolder("Assets/TGameDemo/Scenes")) AssetDatabase.CreateFolder("Assets/TGameDemo", "Scenes");
            if (!AssetDatabase.IsValidFolder("Assets/TGameDemo/Materials")) AssetDatabase.CreateFolder("Assets/TGameDemo", "Materials");
            if (!AssetDatabase.IsValidFolder("Assets/TGameDemo/Scripts")) AssetDatabase.CreateFolder("Assets/TGameDemo", "Scripts");
            if (!AssetDatabase.IsValidFolder("Assets/TGameDemo/Characters")) AssetDatabase.CreateFolder("Assets/TGameDemo", "Characters");
        }
    }
}
#endif
