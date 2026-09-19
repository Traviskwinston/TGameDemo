using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Makes any scene containing a player playable, even one saved before the current
    /// script set existed. Adds whatever components are missing, wires the camera, and
    /// guarantees the room is not completely unlit.
    ///
    /// This is a safety net, not the intended setup path: GameDemo -> Build / Rebuild Demo
    /// Scene still produces the real level.
    /// </summary>
    public static class PlayerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var motor = Object.FindFirstObjectByType<TlouPlayerMotor>();
            if (motor == null)
            {
                return;
            }

            EnsureMinimumLighting();
            GameObject player = motor.gameObject;

            var cover = Require<TlouCover>(player);
            var traversal = Require<TlouTraversal>(player);

            // Avatar first: the weapon looks it up in its own Awake.
            bool hasImported = player.transform.Find("ImportedCharacter") != null;
            HeroAvatar avatar = player.GetComponent<HeroAvatar>();
            if (avatar == null && !hasImported)
            {
                avatar = player.AddComponent<HeroAvatar>();
            }

            var weapon = Require<PlayerWeapon>(player);
            TlouCamera cam = ResolveCamera(motor);

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

            if (cam != null)
            {
                cam.Bind(motor, cover, traversal);
            }
        }

        static T Require<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static TlouCamera ResolveCamera(TlouPlayerMotor motor)
        {
            var cam = Object.FindFirstObjectByType<TlouCamera>();
            if (cam != null)
            {
                return cam;
            }

            Camera main = Camera.main;
            if (main == null)
            {
                var go = new GameObject("OTS_Camera") { tag = "MainCamera" };
                main = go.AddComponent<Camera>();
                main.clearFlags = CameraClearFlags.SolidColor;
                main.backgroundColor = Color.black;
                main.nearClipPlane = 0.04f;
                go.transform.position = motor.transform.position + new Vector3(0.55f, 1.5f, -2.3f);
            }

            if (main.GetComponent<AudioListener>() == null && Object.FindFirstObjectByType<AudioListener>() == null)
            {
                main.gameObject.AddComponent<AudioListener>();
            }

            return main.gameObject.AddComponent<TlouCamera>();
        }

        /// <summary>
        /// A scene with no lights and near-zero ambient renders pure black, which reads as a
        /// broken build. Lift ambient to a floor and add one dim fill so geometry is legible.
        /// </summary>
        static void EnsureMinimumLighting()
        {
            const float floor = 0.08f;
            if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat)
            {
                if (RenderSettings.ambientLight.grayscale < floor)
                {
                    RenderSettings.ambientLight = new Color(floor, floor * 1.05f, floor * 1.25f);
                }
            }
            else if (RenderSettings.ambientSkyColor.grayscale < floor)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(floor, floor * 1.08f, floor * 1.35f);
                RenderSettings.ambientEquatorColor = new Color(floor * 0.8f, floor * 0.82f, floor);
                RenderSettings.ambientGroundColor = new Color(floor * 0.4f, floor * 0.4f, floor * 0.48f);
            }

            bool anyRealtimeLight = false;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.enabled && l.intensity > 0.05f)
                {
                    anyRealtimeLight = true;
                    break;
                }
            }

            if (anyRealtimeLight)
            {
                return;
            }

            var fill = new GameObject("Bootstrap_Fill");
            var light = fill.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.5f, 0.58f, 0.75f);
            light.intensity = 0.35f;
            light.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(52f, 28f, 0f);
            Debug.Log("PlayerBootstrap: scene had no lights, added a dim fill so it is not pure black.");
        }
    }
}
