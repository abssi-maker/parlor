using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// A hand and head provider driven by code instead of by a headset. Used by PlayMode tests and by the
    /// debug autopilot to perform every interaction with "simulated hands": the show logic cannot tell the
    /// difference, because it only ever reads HandSnapshot.
    /// </summary>
    public sealed class ScriptedInput : MonoBehaviour, IHandSource, IHeadSource
    {
        public HandSnapshot Left, Right;
        public Pose HeadPose = new Pose(new Vector3(0, 1.15f, 0), Quaternion.identity);

        public HandSnapshot Get(Handed hand) => hand == Handed.Left ? Left : Right;
        public Pose Head => HeadPose;

        void Write(Handed hand, HandSnapshot s) { if (hand == Handed.Left) Left = s; else Right = s; }

        public void Untrack(Handed hand) => Write(hand, default);

        static HandSnapshot Make(Vector3 palm, Vector3 palmNormal, Vector3 forward, float curl, float pinch)
        {
            forward = Vector3.ProjectOnPlane(forward, palmNormal).normalized;
            Vector3 fingers = Vector3.Lerp(forward * 0.10f, -palmNormal * -0.03f + forward * 0.02f, curl);
            Vector3 index = palm + forward * 0.02f + fingers;
            Vector3 thumb = Vector3.Lerp(palm + Vector3.Cross(palmNormal, forward) * 0.06f + forward * 0.03f, index, pinch);
            return new HandSnapshot
            {
                Tracked = true, PalmPosition = palm, PalmNormal = palmNormal.normalized, PalmForward = forward,
                IndexTip = index, ThumbTip = thumb, Curl = curl, Pinch = pinch,
            };
        }

        /// <summary>Relaxed, tracked hand hovering at a point, palm down.</summary>
        public void Hover(Handed hand, Vector3 palm, Vector3 forward) => Write(hand, Make(palm, Vector3.down, forward, 0.2f, 0f));

        public void OpenPalmUp(Handed hand, Vector3 palm, Vector3 forward) => Write(hand, Make(palm, Vector3.up, forward, 0.08f, 0f));

        public void Fist(Handed hand, Vector3 palm, Vector3 forward) => Write(hand, Make(palm, Vector3.up, forward, 0.92f, 0f));

        public void FlatDown(Handed hand, Vector3 palm, Vector3 forward) => Write(hand, Make(palm, Vector3.down, forward, 0.05f, 0f));

        /// <summary>Pinch with the pinch point (between thumb and index tips) exactly at the given position.</summary>
        public void Pinch(Handed hand, Vector3 pinchPoint, Vector3 forward, bool closed)
        {
            var s = Make(pinchPoint - forward.normalized * 0.12f + Vector3.up * 0.02f, Vector3.down, forward, 0.3f, closed ? 1f : 0f);
            s.IndexTip = pinchPoint + (closed ? Vector3.zero : forward.normalized * 0.02f);
            s.ThumbTip = closed ? pinchPoint : pinchPoint - forward.normalized * 0.03f + Vector3.up * 0.02f;
            Write(hand, s);
        }

        /// <summary>Index finger extended with its tip at the given point (a poke).</summary>
        public void Poke(Handed hand, Vector3 tip, Vector3 forward)
        {
            var s = Make(tip - forward.normalized * 0.14f, Vector3.down, forward, 0.35f, 0f);
            s.IndexTip = tip;
            Write(hand, s);
        }

        public void LookAt(Vector3 eye, Vector3 target) => HeadPose = new Pose(eye, Quaternion.LookRotation((target - eye).normalized, Vector3.up));
    }
}
