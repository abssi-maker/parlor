using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    public enum PalmVariant { Coin, Bird }
    public enum RevealVariant { LiftRise, Placards }
    public enum LessonVariant { Vanish, Reappear }
    public enum TrapdoorSpot { WhereTheCoinLands, WhereTheUserTaps }

    /// <summary>
    /// One caption line. Spoken is the recorded voice clip for exactly this text (vo_<id>, or vo_n<N>_<id> for a
    /// night's override; generated offline by tools/tts_lines.py). Voice is the wordless fallback mood clip, used
    /// only if the spoken clip is missing.
    /// </summary>
    [System.Serializable]
    public struct Line
    {
        public string Id;
        [TextArea] public string Text;
        public string Voice;     // clip name under Resources/Audio, may be empty
        public float Hold;       // seconds on screen; 0 = derive from length
        [System.NonSerialized] public string Spoken;   // set by NightProgram.Get
        public Line(string id, string text, string voice = "", float hold = 0f) { Id = id; Text = text; Voice = voice; Hold = hold; Spoken = null; }
        public float Duration => Hold > 0f ? Hold : Mathf.Clamp(1.1f + Text.Length * 0.052f, 1.6f, 4.5f);
    }

    /// <summary>
    /// A night is the same sequence of beats with different variations and words. Nights are data:
    /// assets live in Resources/Nights (created by the editor tooling); if none are found the built-in
    /// defaults below are used, so the build can never ship without a show.
    /// </summary>
    [CreateAssetMenu(menuName = "PARLOR/Night Program", fileName = "Night")]
    public sealed class NightProgram : ScriptableObject
    {
        public int Night = 1;
        public string Title = "The Borrowed Coin";
        public PalmVariant Palm = PalmVariant.Coin;
        public RevealVariant Reveal = RevealVariant.LiftRise;
        public LessonVariant Lesson = LessonVariant.Vanish;
        public TrapdoorSpot Trapdoor = TrapdoorSpot.WhereTheCoinLands;
        public Color PosterTint = new Color(0.55f, 0.08f, 0.10f);
        public int[] Cards = { 0, 1, 2 };            // indices into the card-face atlas
        public List<Line> Overrides = new List<Line>();   // lines that differ from the base script

        Dictionary<string, Line> _map;

        public Line Get(string id)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, Line>();
                var baseText = new Dictionary<string, string>();
                foreach (var l in Script.Base)
                {
                    var b = l; b.Spoken = "vo_" + l.Id.Replace('.', '_');
                    _map[l.Id] = b; baseText[l.Id] = l.Text;
                }
                foreach (var l in Overrides)
                {
                    var o = l;
                    bool same = baseText.TryGetValue(l.Id, out var t) && t == l.Text;
                    o.Spoken = (same ? "vo_" : "vo_n" + Night + "_") + l.Id.Replace('.', '_');
                    _map[l.Id] = o;
                }
            }
            return _map.TryGetValue(id, out var line) ? line : new Line(id, id);
        }
    }

    /// <summary>The base script (night 1). Every line is captioned and spoken; the caption never depends on the audio.</summary>
    public static class Script
    {
        public static readonly Line[] Base =
        {
            new Line("place.hint",      "Pinch where you'd like the stage.", "voc_ask"),
            new Line("arrive.hello",    "Good evening! The Great Thimble, at your service. Mind the trapdoor.", "voc_greet"),
            new Line("podium.flat",     "Ooh, a podium! You shouldn't have. You didn't, did you? Mine now.", "voc_cheer"),
            new Line("podium.tall",     "The royal box! What a view. Hello up here!", "voc_cheer"),
            new Line("podium.any",      "Don't mind me. Best seat in the house.", "voc_cheer"),
            new Line("arrive.hands",    "Hands up where I can see them. Magicians are a suspicious lot.", "voc_ask"),
            new Line("arrive.begin",    "Ten fingers! My first audience in a hundred years. Let us begin.", "voc_cheer"),
            new Line("return.hello",    "Back again! I kept your card...", "voc_back"),
            new Line("gaze.rule",       "Three cards. Don't touch. Just look at one...", "voc_hush", 3.2f),
            new Line("gaze.remember",   "...and think about it. Loudly.", "voc_short", 2.2f),
            new Line("gaze.got",        "Got it. Don't tell me. I already know.", "voc_tease"),
            new Line("gaze.poke",       "A strong mind! Touch it for me. I won't peek.", "voc_hmm"),
            new Line("coin.lend",       "May I lend you a coin? I'd like it back.", "voc_ask"),
            new Line("coin.pick",       "Pinch it up. Go on, it doesn't bite.", "voc_short"),
            new Line("coin.place",      "Now set it down, anywhere on the table.", "voc_teach"),
            new Line("coin.watch",      "Watch the table...", "voc_hush"),
            new Line("coin.caught",     "Got it!", "voc_cheer"),
            new Line("coin.lean",       "Have a look down here! Mind your nose.", "voc_tease"),
            new Line("palm.offer",      "Offer me your open palm.", "voc_ask"),
            new Line("palm.there",      "There it is. Told you I'd give it back.", "voc_wow"),
            new Line("lesson.intro",    "Now, a secret. Watch closely.", "voc_teach"),
            new Line("lesson.demo",     "Close... and open. Gone!", "voc_teach", 3.0f),
            new Line("lesson.close",    "Close your hand around the coin.", "voc_ask"),
            new Line("lesson.open",     "And open!", "voc_short"),
            new Line("lesson.bravo",    "Bravo! You're a natural.", "voc_cheer"),
            new Line("lesson.friend",   "Remember that move. You may need it sooner than you think.", "voc_tease"),
            // act two: the trick goes wrong
            new Line("mishap.hey",      "Hey! Give that back! That's not in the script!", "voc_wow"),
            new Line("mishap.notme",    "That... wasn't me.", "voc_hmm"),
            new Line("mishap.hide",     "I'll just... supervise. From back here.", "voc_hush"),
            new Line("mishap.hold",     "Quick! Hands flat on the door! Hold it shut!", "voc_ask"),
            new Line("mishap.steady",   "Steady... it's only a small monster. Mostly.", "voc_hush"),
            new Line("mishap.dontlet",  "Whatever you do, don't let go! One... two...", "voc_teach"),
            new Line("mishap.dragged",  "No no no no NO!", "voc_wow"),
            new Line("mishap.monster",  "A hundred years in the dark while HE takes the bows! The coin is MINE. And so is your CARD!", "voc_hmm", 5.2f),
            new Line("mishap.help",     "Psst! It's me! My shadow's gone sour! Quick, do the Vanish!", "voc_ask"),
            new Line("mishap.close",    "Close your fist on it!", "voc_ask"),
            new Line("mishap.open",     "And OPEN!", "voc_short"),
            new Line("mishap.ahem",     "Ahem. All... part of the act.", "voc_hmm"),
            new Line("mishap.pity",     "Poor old thing. A hundred years under the stage, and never once a bow.", "voc_hush"),
            new Line("mishap.coin",     "Your coin. Keep it. You earned it.", "voc_tease"),
            new Line("finale.cover",    "Cover the velvet case with a flat hand.", "voc_ask"),
            new Line("finale.lift",     "Now lift.", "voc_short"),
            new Line("finale.empty",    "Empty! Then where is your card?", "voc_hmm"),
            new Line("finale.yours",    "Is this your card? Of course it is.", "voc_wow"),
            new Line("curtain.star",    "You saved the show! Tonight, the star is you.", "voc_star"),
            new Line("curtain.shadow",  "Wait. Someone's missing. Come on out, old friend. Tonight, you get a bow too.", "voc_star"),
            new Line("curtain.bow",     "Take a bow! All of us!", "voc_cheer"),
            new Line("encore.tomorrow", "Tomorrow night, your coin learns to fly. Bring an open palm.", "voc_bye", 3.6f),
            new Line("encore.again",    "Touch the coin to see it again.", "voc_short"),
            new Line("hint.pinch",      "Or simply pinch.", "voc_short"),
            new Line("hint.hands",      "Hold your hands where I can see them.", "voc_hmm"),
            new Line("resume",          "Where were we? Ah yes. Magic.", "voc_hmm"),
        };
    }

    public static class NightCatalog
    {
        static NightProgram[] _nights;

        /// <summary>Night 1, 2, 3 in order. Night 4 and later cycle through them in a shuffled order.</summary>
        public static NightProgram For(int night)
        {
            if (_nights == null) Load();
            if (night <= _nights.Length) return _nights[Mathf.Max(1, night) - 1];
            int cycle = (night - 1) / _nights.Length;
            int slot = (night - 1) % _nights.Length;
            return _nights[(slot + cycle) % _nights.Length];   // rotate the order each cycle
        }

        public static int Count { get { if (_nights == null) Load(); return _nights.Length; } }

        static void Load()
        {
            var found = Resources.LoadAll<NightProgram>("Nights");
            if (found != null && found.Length >= 3)
            {
                System.Array.Sort(found, (a, b) => a.Night.CompareTo(b.Night));
                _nights = found;
            }
            else _nights = BuildDefaults();
        }

        public static NightProgram[] BuildDefaults()
        {
            var n1 = ScriptableObject.CreateInstance<NightProgram>();
            n1.name = "Night1"; n1.Night = 1; n1.Title = "The Borrowed Coin";

            var n2 = ScriptableObject.CreateInstance<NightProgram>();
            n2.name = "Night2"; n2.Night = 2; n2.Title = "The Coin Takes Wing";
            n2.Palm = PalmVariant.Bird; n2.Reveal = RevealVariant.Placards;
            n2.PosterTint = new Color(0.10f, 0.20f, 0.45f); n2.Cards = new[] { 3, 4, 5 };
            n2.Overrides = new List<Line>
            {
                new Line("return.hello",    "Back again! I kept your card...", "voc_back"),
                new Line("gaze.got",        "Not the same card again, surely?", "voc_tease"),
                new Line("palm.there",      "A coin? Tonight it has wings.", "voc_wow"),
                new Line("finale.yours",    "Ask the audience!", "voc_wow"),
                new Line("mishap.notme",    "Oh no. Not again.", "voc_hmm"),
                new Line("mishap.pity",     "Sulking again. He does so love an audience.", "voc_hush"),
                new Line("curtain.shadow",  "And you, old friend. Don't be shy. Your bow.", "voc_star"),
                new Line("encore.tomorrow", "Tomorrow, you choose where my door opens.", "voc_bye", 3.6f),
            };

            var n3 = ScriptableObject.CreateInstance<NightProgram>();
            n3.name = "Night3"; n3.Night = 3; n3.Title = "The Wandering Door";
            n3.Lesson = LessonVariant.Reappear; n3.Trapdoor = TrapdoorSpot.WhereTheUserTaps;
            n3.PosterTint = new Color(0.08f, 0.32f, 0.22f); n3.Cards = new[] { 6, 7, 8 };
            n3.Overrides = new List<Line>
            {
                new Line("return.hello",    "You again! Wonderful. Tonight, a new door.", "voc_back"),
                new Line("coin.place",      "Set it down, then tap the table where you'd like my door.", "voc_teach", 3.8f),
                new Line("lesson.intro",    "Tonight: the Reappear. Watch closely.", "voc_teach"),
                new Line("lesson.demo",     "An empty fist... and open. It's back!", "voc_teach", 3.0f),
                new Line("lesson.close",    "Close your empty hand.", "voc_ask"),
                new Line("mishap.notme",    "It's back. Of course it's back.", "voc_hmm"),
                new Line("mishap.pity",     "He'll be back tomorrow. He always is. I'd miss him if he weren't.", "voc_hush"),
                new Line("curtain.shadow",  "Come on then, partner. Same as always: together.", "voc_star"),
                new Line("encore.tomorrow", "Come back tomorrow. I never repeat myself. Almost never.", "voc_bye", 3.8f),
            };
            return new[] { n1, n2, n3 };
        }
    }
}
