namespace Parlor
{
    public enum GateEvent { None, Hint, AltOffered, Success, AltSuccess, Timeout }

    /// <summary>
    /// The wait-for-the-user primitive. Pure and tick-driven so EditMode tests can run it without a scene.
    ///
    /// A gate succeeds when its pose has been held for HoldSeconds. "Held" uses hysteresis: once the strict
    /// (enter) form of the pose is seen, the loose (stay) form keeps the hold alive, so a wobbling hand
    /// does not reset the timer. Independently of the pose:
    ///   at HintAt  → Hint        (the magician demonstrates; fired once)
    ///   at AltAt   → AltOffered  (a pinch anywhere now counts; fired once)
    ///   at AutoAt  → Timeout     (the beat advances on its own)
    /// Time only runs while input is available: losing hand tracking pauses the gate instead of failing it.
    /// </summary>
    public sealed class BeatGate
    {
        public readonly float HintAt, AltAt, AutoAt, HoldSeconds;

        public float Elapsed { get; private set; }
        public float Held { get; private set; }
        public bool HintGiven { get; private set; }
        public bool AltOpen { get; private set; }
        public bool Done { get; private set; }
        public GateEvent Result { get; private set; }

        bool _engaged;      // strict pose has been seen and not yet dropped
        bool _pinchArmed;   // alt pinch must be a fresh pinch, not one already held when the alt opened

        public BeatGate(float hintAt, float altAt, float autoAt, float holdSeconds = 0.4f)
        { HintAt = hintAt; AltAt = altAt; AutoAt = autoAt; HoldSeconds = holdSeconds; }

        public BeatGate(in BeatSpec spec, float holdSeconds = 0.4f) : this(spec.HintAt, spec.AltAt, spec.AutoAt, holdSeconds) { }

        /// <param name="enter">strict pose test this frame</param>
        /// <param name="stay">loose pose test this frame (must be true whenever enter is)</param>
        /// <param name="pinch">any hand pinching this frame</param>
        /// <param name="inputAvailable">false while hand tracking is lost or the app is paused</param>
        public GateEvent Tick(float dt, bool enter, bool stay, bool pinch, bool inputAvailable = true)
        {
            if (Done) return GateEvent.None;
            if (!inputAvailable) { _engaged = false; Held = 0f; return GateEvent.None; }

            Elapsed += dt;

            if (enter) _engaged = true;
            else if (!stay) _engaged = false;
            Held = _engaged ? Held + dt : 0f;
            if (Held >= HoldSeconds) return Finish(GateEvent.Success);

            if (AltOpen)
            {
                if (!pinch) _pinchArmed = true;
                else if (_pinchArmed) return Finish(GateEvent.AltSuccess);
            }

            if (Elapsed >= AutoAt) return Finish(GateEvent.Timeout);

            if (!AltOpen && Elapsed >= AltAt) { AltOpen = true; _pinchArmed = !pinch; return GateEvent.AltOffered; }
            if (!HintGiven && Elapsed >= HintAt) { HintGiven = true; return GateEvent.Hint; }
            return GateEvent.None;
        }

        /// <summary>Force the gate through (debug autopilot, skip paths).</summary>
        public GateEvent Complete() => Done ? GateEvent.None : Finish(GateEvent.Success);

        GateEvent Finish(GateEvent e) { Done = true; Result = e; return e; }
    }
}
