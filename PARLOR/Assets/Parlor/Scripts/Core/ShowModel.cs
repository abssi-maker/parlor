using System;

namespace Parlor
{
    /// <summary>
    /// Pure show state: which beat we are in, which night it is, which card was chosen.
    /// No Unity scene access, so the graph can be exercised in EditMode tests.
    /// </summary>
    public sealed class ShowModel
    {
        public ShowState State { get; private set; } = ShowGraph.First;
        public int Night { get; private set; } = 1;
        public bool Returning => Night >= 2;
        public int ChosenCard = -1;         // index into the night's three cards; -1 until the gaze pick
        public bool PalmCoinCut;            // cut list §8.8
        public int Runs { get; private set; }

        public event Action<ShowState, ShowState> Changed;

        public ShowModel(int night = 1) { Night = Math.Max(1, night); }

        public ShowState Advance()
        {
            var prev = State;
            var next = ShowGraph.Get(State).Next;
            while (next != ShowState.Encore && IsCut(next)) next = ShowGraph.Get(next).Next;
            if (prev == ShowState.CurtainCall) Runs++;
            State = next;
            Changed?.Invoke(prev, next);
            return next;
        }

        /// <summary>Restart the current night from Arrival (Encore token) or from Placement (recenter, anchor lost).</summary>
        public void Restart(bool replace = false)
        {
            var prev = State;
            State = replace ? ShowState.Placement : ShowState.Arrival;
            ChosenCard = -1;
            Changed?.Invoke(prev, State);
        }

        public void SetNight(int night) { Night = Math.Max(1, night); }

        bool IsCut(ShowState s) => s == ShowState.PalmCoin && PalmCoinCut && ShowGraph.Get(s).Cuttable;
    }
}
