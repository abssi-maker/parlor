using System.Collections.Generic;

namespace Parlor
{
    /// <summary>The beats of one night, in order. ShowDirector plays them; ShowModel walks the graph.</summary>
    public enum ShowState
    {
        Placement = 0,   // or Restore on a returning night
        Arrival,         // or ReturnKnock on a returning night
        GazeCardPick,
        BorrowedCoin,
        PalmCoin,
        Lesson,
        CardFinale,
        CurtainCall,
        Encore,
    }

    /// <summary>
    /// Timing contract of one beat. Every beat that waits on the user has all three escape hatches,
    /// which is what "never softlock" means in practice:
    /// hint (the magician demonstrates) → alternative (pinch anywhere) → auto-advance.
    /// </summary>
    public readonly struct BeatSpec
    {
        public readonly ShowState State;
        public readonly ShowState Next;
        public readonly float HintAt, AltAt, AutoAt;
        public readonly bool Cuttable;   // may be skipped by the cut list (§8) without breaking the graph

        public BeatSpec(ShowState state, ShowState next, float hintAt, float altAt, float autoAt, bool cuttable = false)
        { State = state; Next = next; HintAt = hintAt; AltAt = altAt; AutoAt = autoAt; Cuttable = cuttable; }
    }

    public static class ShowGraph
    {
        public const float Hint = 6f, Alt = 15f, Auto = 30f;

        static readonly Dictionary<ShowState, BeatSpec> Specs = new Dictionary<ShowState, BeatSpec>
        {
            // Placement resolves itself (tier 1 → 2 → 3). Tier 2 waits for a pinch, so it gets a short fuse to tier 3.
            { ShowState.Placement,    new BeatSpec(ShowState.Placement,    ShowState.Arrival,      Hint, 12f, 20f) },
            { ShowState.Arrival,      new BeatSpec(ShowState.Arrival,      ShowState.GazeCardPick, Hint, Alt, Auto) },
            { ShowState.GazeCardPick, new BeatSpec(ShowState.GazeCardPick, ShowState.BorrowedCoin, Hint, Alt, Auto) },
            { ShowState.BorrowedCoin, new BeatSpec(ShowState.BorrowedCoin, ShowState.PalmCoin,     Hint, Alt, Auto) },
            { ShowState.PalmCoin,     new BeatSpec(ShowState.PalmCoin,     ShowState.Lesson,       Hint, Alt, Auto, cuttable: true) },
            { ShowState.Lesson,       new BeatSpec(ShowState.Lesson,       ShowState.CardFinale,   Hint, Alt, Auto) },
            { ShowState.CardFinale,   new BeatSpec(ShowState.CardFinale,   ShowState.CurtainCall,  Hint, Alt, Auto) },
            { ShowState.CurtainCall,  new BeatSpec(ShowState.CurtainCall,  ShowState.Encore,       Hint, Alt, Auto) },
            // Encore is the resting state: the show is complete. Poking the token restarts at Arrival.
            { ShowState.Encore,       new BeatSpec(ShowState.Encore,       ShowState.Arrival,      Hint, Alt, float.PositiveInfinity) },
        };

        public static BeatSpec Get(ShowState s) => Specs[s];
        public static IEnumerable<BeatSpec> All => Specs.Values;
        public const ShowState First = ShowState.Placement;
        public const ShowState Last = ShowState.Encore;
    }
}
