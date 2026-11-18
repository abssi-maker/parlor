#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Editor only (video capture). Writes, ~30 times a second, a head pose that keeps the star of the moment centred in
    /// view: the magician, or his Shadow while it flees along the wall. The capture script's "follow on" (tools/xrop.py)
    /// feeds it to the simulated head. Turning only: the head stays where the script seated it, so the show's lean and
    /// distance checks are untouched. During the two beats that read head rotation (the gaze card pick and the bow) the
    /// line starts with "pause" and the script's own head moves run instead.
    /// File: PARLOR/Temp/parlor_follow.pose: "mode  follow pose (x y z qx qy qz qw, OpenXR)  current head pose (same)".
    /// </summary>
    public sealed class CaptureFollow : MonoBehaviour
    {
        const float SmoothTime = 0.45f;
        static string PathOut => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/parlor_follow.pose"));

        Vector3 _aim, _vel;
        bool _has;
        float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("CaptureFollow (editor)") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<CaptureFollow>();
        }

        void LateUpdate()
        {
            var app = ParlorApp.Instance;
            if (!app || app.Head == null || !app.Director || !app.Director.Magician) { _has = false; return; }
            var d = app.Director;
            Vector3 target = Target(d);
            if (!_has) { _aim = target; _vel = Vector3.zero; _has = true; }
            else _aim = Vector3.SmoothDamp(_aim, target, ref _vel, SmoothTime, Mathf.Infinity, Time.deltaTime);
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / 30f;

            Pose head = app.Head.Head;
            if ((_aim - head.position).sqrMagnitude < 1e-4f) return;
            Quaternion look = AimLeftEye(head.position, _aim);
            var state = d.Model != null ? d.Model.State : ShowState.Placement;
            bool pause = state == ShowState.GazeCardPick || d.ActiveGateName == "curtain.bow";
            string line = (pause ? "pause " : "follow ") + Fmt(head.position, look) + " " + Fmt(head.position, head.rotation);
            try { File.WriteAllText(PathOut, line); } catch (IOException) { }
        }

        /// <summary>The point to centre: his upper body (the hat stays in frame), the fleeing Shadow, or between him and
        /// his returning Shadow at the curtain call.</summary>
        static Vector3 Target(ShowDirector d)
        {
            var m = d.Magician;
            Vector3 feet = m.transform.position;
            Vector3 hat = m.HeadBone.TransformPoint(new Vector3(0f, 0.15f, 0f));
            Vector3 him = Vector3.Lerp(feet, hat, 0.6f);
            // on the trapdoor lift (the arrival, the Mishap's rise) he can be hidden far under the table: frame the
            // trapdoor, rising with him once he is above it
            if (m.transform.parent && m.transform.parent.name == "Lift" && d.Portal)
            {
                Vector3 door = d.Portal.transform.position;
                him = new Vector3(door.x, Mathf.Max(door.y + 0.04f, him.y), door.z);
            }
            var s = d.Shadow;
            if (s && s.Shown && d.Model != null)
            {
                if (d.Model.State == ShowState.Mishap && !m.IsMonster) return s.Position;     // it runs off along the wall
                if (d.Model.State == ShowState.CurtainCall) return Vector3.Lerp(him, s.Position, 0.4f);
            }
            return him;
        }

        /// <summary>
        /// Head rotation that puts the target at the centre of the LEFT eye's image, which is what the capture records.
        /// That centre is not on the head's forward line: the eye sits ~3 cm left of the head and its frustum is
        /// asymmetric (on the simulator, the forward line lands ~150 px right of the image centre), so aiming the head
        /// straight at the target leaves it off to the right.
        /// </summary>
        static Quaternion AimLeftEye(Vector3 headPos, Vector3 target)
        {
            Vector3 centreRay = Vector3.forward, eyeOffset = Vector3.zero;
            var cam = Camera.main;
            if (cam && cam.stereoEnabled)
            {
                Matrix4x4 P = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                // view space looks down -z: NDC (0,0) is the ray x = m02/m00, y = m12/m11 at z = -1, i.e. (that x, that y, +1)
                // in the camera's own +z-forward frame (about 15° left on the simulator)
                centreRay = new Vector3(P.m02 / P.m00, P.m12 / P.m11, 1f).normalized;
                Vector3 eyeWorld = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint(Vector3.zero);
                eyeOffset = cam.transform.InverseTransformPoint(eyeWorld);
            }
            Quaternion toCentre = Quaternion.Inverse(Quaternion.LookRotation(centreRay, Vector3.up));
            Quaternion q = Quaternion.LookRotation((target - headPos).normalized, Vector3.up);
            for (int i = 0; i < 3; i++)
            {
                Vector3 eye = headPos + q * eyeOffset;
                q = Quaternion.LookRotation((target - eye).normalized, Vector3.up) * toCentre;
            }
            return q;
        }

        static string Fmt(Vector3 p, Quaternion q) =>
            // Unity (left-handed, +Z forward) → OpenXR (right-handed, -Z forward), as DebugSeat does
            string.Format(CultureInfo.InvariantCulture, "{0:F4} {1:F4} {2:F4} {3:F5} {4:F5} {5:F5} {6:F5}", p.x, p.y, -p.z, -q.x, -q.y, q.z, q.w);
    }
}
#endif
