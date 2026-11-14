using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// "Think of a card": works out which of N cards the user is looking at from the head pose alone.
    /// Nothing is ever drawn for this; the measurement is invisible by design.
    ///
    /// Quest 3/3S have no eye tracking, so this uses the head-forward ray. People turn their head only part
    /// of the way to a target and finish with their eyes, so the head's horizontal bearing is amplified by
    /// HeadGain before it is compared with the cards' bearings. Cards sit in one horizontal row, so only the
    /// horizontal bearing discriminates; pitch is just a loose "is the user looking at the row at all" check.
    /// </summary>
    public sealed class GazeDwellPicker
    {
        public const float HeadGain = 1.35f;
        public const float MaxPitchErrorDeg = 28f;
        public const float AmbiguityMargin = 0.15f;   // top two within 15% → ask for a poke instead
        public const float MinTotalDwell = 0.8f;      // seconds actually spent looking at the row

        readonly float[] _dwell;
        public float[] Dwell => _dwell;
        public float Total { get; private set; }

        public GazeDwellPicker(int cards) { _dwell = new float[cards]; }

        public void Reset() { for (int i = 0; i < _dwell.Length; i++) _dwell[i] = 0f; Total = 0f; }

        /// <param name="cards">card centres in world space, in row order</param>
        /// <param name="up">stage up axis (the row's bearing is measured around it)</param>
        public void Accumulate(Pose head, Vector3[] cards, Vector3 up, float dt)
        {
            if (cards == null || cards.Length != _dwell.Length) return;

            Vector3 centre = Vector3.zero;
            for (int i = 0; i < cards.Length; i++) centre += cards[i];
            centre /= cards.Length;

            Vector3 toCentre = centre - head.position;
            Vector3 fwd = head.rotation * Vector3.forward;

            float pitchErr = Mathf.Abs(Elevation(fwd, up) - Elevation(toCentre, up));
            if (pitchErr > MaxPitchErrorDeg) return;

            Vector3 flatCentre = Vector3.ProjectOnPlane(toCentre, up);
            float headBearing = Vector3.SignedAngle(flatCentre, Vector3.ProjectOnPlane(fwd, up), up) * HeadGain;

            int best = -1; float bestErr = float.MaxValue;
            for (int i = 0; i < cards.Length; i++)
            {
                float b = Vector3.SignedAngle(flatCentre, Vector3.ProjectOnPlane(cards[i] - head.position, up), up);
                float err = Mathf.Abs(b - headBearing);
                if (err < bestErr) { bestErr = err; best = i; }
            }
            if (best < 0) return;
            _dwell[best] += dt;
            Total += dt;
        }

        /// <summary>Index of the card with the most dwell, and whether the result is too close to call.</summary>
        public int Pick(out bool ambiguous)
        {
            int top = 0;
            for (int i = 1; i < _dwell.Length; i++) if (_dwell[i] > _dwell[top]) top = i;
            float second = 0f;
            for (int i = 0; i < _dwell.Length; i++) if (i != top && _dwell[i] > second) second = _dwell[i];
            ambiguous = Total < MinTotalDwell || _dwell[top] <= 0f || (_dwell[top] - second) / _dwell[top] < AmbiguityMargin;
            return top;
        }

        static float Elevation(Vector3 v, Vector3 up) =>
            Mathf.Asin(Mathf.Clamp(Vector3.Dot(v.normalized, up), -1f, 1f)) * Mathf.Rad2Deg;
    }
}
