using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Pose predicates. Thresholds are deliberately wide: the simulator cannot validate fine finger tracking,
    /// so every pose must be recognised from a sloppy, half-formed version of it on a real Quest.
    /// Each predicate has an "enter" form and a looser "stay" form; PoseGate uses the pair for hysteresis.
    /// </summary>
    public static class PoseMath
    {
        // cos of the allowed deviation from straight up/down
        const float EnterCos = 0.50f;   // within 60 degrees
        const float StayCos = 0.26f;    // within 75 degrees
        const float OpenCurlEnter = 0.45f, OpenCurlStay = 0.60f;
        // A pointing hand (three fingers curled) measures about 0.75, so a fist must be clearly above that to start.
        const float FistCurlEnter = 0.82f, FistCurlStay = 0.55f;

        public static bool PalmUpOpen(in HandSnapshot h, bool stay = false) =>
            h.Tracked && Vector3.Dot(h.PalmNormal, Vector3.up) > (stay ? StayCos : EnterCos)
                      && h.Curl < (stay ? OpenCurlStay : OpenCurlEnter);

        public static bool PalmDownFlat(in HandSnapshot h, bool stay = false) =>
            h.Tracked && Vector3.Dot(h.PalmNormal, Vector3.down) > (stay ? StayCos : EnterCos)
                      && h.Curl < (stay ? OpenCurlStay : OpenCurlEnter);

        public static bool Fist(in HandSnapshot h, bool stay = false) =>
            h.Tracked && h.Curl > (stay ? FistCurlStay : FistCurlEnter);

        public static bool Open(in HandSnapshot h, bool stay = false) =>
            h.Tracked && h.Curl < (stay ? OpenCurlStay : OpenCurlEnter);

        /// <summary>Flat hand hovering over a point on the table (the velvet case), within a generous cylinder.</summary>
        public static bool CoversPoint(in HandSnapshot h, Vector3 point, Vector3 up, float radius, float maxHeight, bool stay = false)
        {
            if (!PalmDownFlat(h, stay)) return false;
            Vector3 d = h.PalmPosition - point;
            float height = Vector3.Dot(d, up);
            float lateral = (d - up * height).magnitude;
            float slack = stay ? 1.35f : 1f;
            return height > -0.03f && height < maxHeight * slack && lateral < radius * slack;
        }

        /// <summary>Head-dip ("bow") amount in degrees relative to a reference forward pitch.</summary>
        public static float PitchDownDegrees(Quaternion head)
        {
            Vector3 f = head * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
