#if UNITY_EDITOR
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Editor only. When the open scene has no XR camera rig, runs the show on a plain camera with scripted hands,
    /// so the show logic, shaders and audio can be checked without a headset or the simulator. Never in a build.
    /// </summary>
    public static class DesktopHarness
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (ParlorApp.Instance) return;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb && mb.GetType().Name == "OVRCameraRig") return;   // the Quest layer owns this scene

            var args = PlayerPrefs.GetString(ParlorOptions.EditorArgsKey, "");
            if (!args.Contains("-parlorDesktop")) return;

            var input = new GameObject("ScriptedInput").AddComponent<ScriptedInput>();
            // a seated user looking slightly down at the table in front of them
            Vector3 eye = new Vector3(0, 1.15f, 0);
            input.LookAt(eye, eye + new Vector3(0, -0.36f, 0.57f));

            var cam = Camera.main;
            if (!cam)
            {
                var go = new GameObject("DesktopCamera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.transform.SetPositionAndRotation(input.HeadPose.position, input.HeadPose.rotation);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f; cam.fieldOfView = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);   // stands in for passthrough
            var follow = cam.gameObject.AddComponent<FollowHead>();
            follow.Input = input;

            var options = ParlorOptions.FromEnvironment();
            ParlorApp.Launch(input, input, null, options);
        }

        sealed class FollowHead : MonoBehaviour
        {
            public ScriptedInput Input;
            void LateUpdate() { transform.SetPositionAndRotation(Input.HeadPose.position, Input.HeadPose.rotation); }
        }
    }
}
#endif
