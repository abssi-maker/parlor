using UnityEngine;

namespace Parlor
{
    public enum Handed { Left = 0, Right = 1 }

    /// <summary>
    /// One hand, one frame, reduced to what the show needs. Everything downstream (pose gates, grab, poke)
    /// reads this struct, never the SDK. That keeps the show testable without a headset: the simulator and
    /// the scripted test provider both just fill in snapshots.
    /// </summary>
    public struct HandSnapshot
    {
        public bool Tracked;
        public Vector3 PalmPosition;    // centre of the palm, world space
        public Vector3 PalmNormal;      // points out of the palm (the way an offered palm "faces")
        public Vector3 PalmForward;     // wrist towards the fingers
        public Vector3 IndexTip;
        public Vector3 ThumbTip;
        public float Pinch;             // 0..1 index-thumb pinch strength
        public float Curl;              // 0 = fingers straight, 1 = fist (average of the four fingers)

        public bool IsPinching => Tracked && Pinch > 0.7f;
        public Vector3 PinchPoint => (IndexTip + ThumbTip) * 0.5f;
    }

    public interface IHandSource
    {
        HandSnapshot Get(Handed hand);
    }

    public interface IHeadSource
    {
        /// <summary>World pose of the centre eye.</summary>
        Pose Head { get; }
    }

    public static class HandSourceExtensions
    {
        public static bool AnyTracked(this IHandSource s) => s.Get(Handed.Left).Tracked || s.Get(Handed.Right).Tracked;
        public static bool BothTracked(this IHandSource s) => s.Get(Handed.Left).Tracked && s.Get(Handed.Right).Tracked;

        /// <summary>First tracked hand satisfying the predicate, right hand preferred on a tie.</summary>
        public static bool TryFind(this IHandSource s, System.Func<HandSnapshot, bool> pred, out HandSnapshot found, out Handed which)
        {
            for (int i = 1; i >= 0; i--)
            {
                var h = s.Get((Handed)i);
                if (h.Tracked && pred(h)) { found = h; which = (Handed)i; return true; }
            }
            found = default; which = Handed.Right; return false;
        }
    }
}
