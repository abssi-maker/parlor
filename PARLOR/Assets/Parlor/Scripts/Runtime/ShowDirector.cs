using System;
using System.Collections;
using UnityEngine;

namespace Parlor
{
    /// <summary>What the placement step tells the show about the place it found.</summary>
    public sealed class StageSite
    {
        public int Tier = 3;                                   // 1 scene table, 2 pinch-placed surface, 3 floating table
        /// <summary>Usable table area around the stage origin, in stage XZ (x = right, y = away from the user).</summary>
        public Rect TableRect = new Rect(-0.46f, -0.32f, 0.92f, 0.64f);
        public bool Restored;                                  // came back from a saved anchor
        public string TableUuid = "";
    }

    /// <summary>
    /// Runs the show: Placement → Arrival → GazeCardPick → BorrowedCoin → PalmCoin → Lesson → CardFinale →
    /// CurtainCall → Encore. ShowModel decides the order; this class stages each beat.
    ///
    /// Rules every beat follows:
    ///  * anything that waits for the user goes through a BeatGate (hint at 6 s, pinch alternative at 15 s,
    ///    auto-advance at 30 s), so no beat can softlock;
    ///  * a beat can be entered cold: EnterCanonical(state) puts the stage in that beat's starting condition,
    ///    which is what makes pause/resume ("Where were we?"), Encore restart and debug jumps clean;
    ///  * the show only reads hands and head through IHandSource / IHeadSource.
    /// </summary>
    public sealed class ShowDirector : MonoBehaviour
    {
        // wired by ParlorApp
        public Transform Stage;
        public TablePortal Portal;
        public MagicianActor Magician;
        public CoinProp Coin;
        public VelvetCase Case;
        public EncoreToken Token;
        public CaptionPresenter Captions;
        public AudioDirector Audio;
        public WallCurtainStager Curtains;
        public TitleCard Title;
        public BirdProp Bird;
        public IHandSource Hands;
        public IHeadSource Head;
        public StageSite Site = new StageSite();
        public Func<IEnumerator> PlaceStage;         // provided by StagePlacer; fills Site and positions Stage
        public Action<int, int> NightCompleted;      // (night, card face) → persistence
        public Action DebugGesture;                  // hidden: 3 s pinch on the Encore token

        public ShowModel Model { get; private set; }
        public NightProgram Night { get; private set; }
        public bool OneHanded;
        /// <summary>Debug autopilot: gates complete themselves shortly after opening. Never enabled in a release build.</summary>
        public bool Autopilot;
        public float AutopilotDelay = 0.6f;
        public bool Paused { get; private set; }
        public BeatGate ActiveGate { get; private set; }
        public string ActiveGateName { get; private set; } = "";
        public int BeatsCompleted { get; private set; }
        public float StartupSeconds { get; private set; } = -1f;   // launch → magician visible (gate G1)
        public event Action<ShowState> BeatStarted;
        public event Action<string> Log;

        CardProp[] _cards = new CardProp[3];
        CardProp _bigCard;
        ParticleSystem _sparkle, _dust, _roses;
        Vector3 _portalHome;
        float _handsLostFor, _lastHandsNag;
        bool _resumePending;
        const float LiftTop = -0.004f;        // just under the table plane, so the stage floor is clipped to the opening
        float LiftBottom => -StageDepth;
        /// <summary>How far down the lift carries him for the catch: deep enough to read as "far below", near enough to see his face.</summary>
        public const float StageDepth = 0.62f;

        // ------------------------------------------------------------------ lifecycle

        public void Init(int night)
        {
            Model = new ShowModel(night);
            Night = NightCatalog.For(night);
            _portalHome = Vector3.zero;
            _sparkle = Sparkles.Create(Stage, "Sparkle", new Color(1f, 0.84f, 0.45f, 0.9f), 0.012f, 0.9f, 220);
            _dust = Sparkles.Create(Stage, "Dust", new Color(1f, 0.9f, 0.7f, 0.35f), 0.004f, 5f, 40);
            _roses = Sparkles.Create(Stage, "Roses", new Color(0.95f, 0.10f, 0.16f, 0.95f), 0.030f, 2.6f, 60, portalClipped: false, gravity: 0.25f);
            for (int i = 0; i < 3; i++)
            {
                _cards[i] = CardProp.Create(Stage, Night.Cards[i]);
                _cards[i].gameObject.SetActive(false);
            }
        }

        public void Begin()
        {
            StopAllCoroutines();
            ActiveGate = null; ActiveGateName = "";
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            while (true)
            {
                var state = Model.State;
                EnterCanonical(state);
                BeatStarted?.Invoke(state);
                Note("beat " + state + " (night " + Model.Night + ")");
                if (_resumePending && state != ShowState.Placement)
                {
                    _resumePending = false;
                    yield return Say("resume");
                }
                switch (state)
                {
                    case ShowState.Placement: yield return Placement(); break;
                    case ShowState.Arrival: yield return Arrival(); break;
                    case ShowState.GazeCardPick: yield return GazeCardPick(); break;
                    case ShowState.BorrowedCoin: yield return BorrowedCoin(); break;
                    case ShowState.PalmCoin: yield return PalmCoin(); break;
                    case ShowState.Lesson: yield return Lesson(); break;
                    case ShowState.CardFinale: yield return CardFinale(); break;
                    case ShowState.CurtainCall: yield return CurtainCall(); break;
                    case ShowState.Encore: yield return Encore(); break;
                }
                BeatsCompleted++;
                Model.Advance();
            }
        }

        /// <summary>Focus lost, headset removed, or app paused: freeze the show and its audio.</summary>
        public void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (Audio) Audio.SetPaused(paused);
            Note(paused ? "paused" : "resumed");
            if (!paused && Model != null && Model.State != ShowState.Placement && Model.State != ShowState.Encore)
            {
                // "Where were we?" then replay the current beat from its start.
                _resumePending = true;
                Begin();
            }
        }

        /// <summary>Jump to a beat (debug launch arg, tests). The stage is put in that beat's starting condition.</summary>
        public void JumpTo(ShowState state)
        {
            while (Model.State != state) Model.Advance();
            Begin();
        }

        public void SkipBeat() { if (ActiveGate != null) ActiveGate.Complete(); }

        void Note(string msg) { Log?.Invoke(msg); Debug.Log("[PARLOR] " + msg); }

        // ------------------------------------------------------------------ canonical beat start

        /// <summary>Puts every prop where it belongs at the start of the given beat.</summary>
        void EnterCanonical(ShowState state)
        {
            bool onStage = state != ShowState.Placement && state != ShowState.Arrival;
            Portal.transform.localPosition = _portalHome;
            Portal.SetWiden(1f);
            Portal.SetOpen(onStage ? 1f : 0f);
            Portal.LiftY = onStage ? LiftTop : LiftBottom;
            Portal.SetSpot(state == ShowState.Placement ? 0f : 0.6f);
            Portal.Theatre.Placards.gameObject.SetActive(false);

            Magician.transform.SetParent(Portal.Lift, false);
            Magician.transform.localPosition = Vector3.zero;
            Magician.SetVisible(state != ShowState.Placement);
            Magician.Face(180f, true);
            Magician.Idle();
            Magician.Talking = false;
            Magician.RightHand.localScale = Vector3.one;

            bool propsOut = onStage;
            Case.gameObject.SetActive(propsOut);
            Case.transform.localPosition = ClampToTable(Layout.Case, 0.06f);
            Case.transform.localRotation = Quaternion.Euler(0, -14, 0);
            Case.SetOpen(false);

            foreach (var c in _cards) if (c) c.gameObject.SetActive(false);
            if (_bigCard) _bigCard.gameObject.SetActive(false);
            if (Bird) Bird.Hide();

            // from PalmCoin on, the magician is holding the coin
            bool held = state == ShowState.PalmCoin || state == ShowState.Lesson;
            Coin.Spin = 0f; Coin.SetGlow(0f);
            if (held) GiveCoinToMagician(); else { Coin.transform.SetParent(Stage, false); Coin.SetVisible(false); }

            Token.Shown = false;
            if (Curtains) Curtains.HideImmediate();
            if (Title) Title.HideImmediate();
            Captions.Hide();
            Sparkles.Trail(_sparkle, false);
            Sparkles.Trail(_dust, onStage, 5f);
            _dust.transform.localPosition = new Vector3(0, 0.06f, 0);
            if (Audio && state != ShowState.Placement) Audio.StartMusic(Portal.transform.TransformPoint(0, -0.5f, 0));
        }

        void GiveCoinToMagician()
        {
            Coin.transform.SetParent(Magician.RightHand, false);
            Coin.transform.localPosition = new Vector3(0, -0.012f, 0.008f);
            Coin.transform.localRotation = Quaternion.Euler(70, 0, 0);
            Coin.SetVisible(true);
        }

        // ------------------------------------------------------------------ shared helpers

        Vector3 MagicianHead => Magician.HeadBone.position;

        /// <summary>A line's caption must stay up for as long as its recorded voice lasts.</summary>
        Line Timed(string id, out float voice)
        {
            var line = Night.Get(id);
            voice = Audio ? Audio.SpokenLength(line) : 0f;
            if (voice > 0f) line.Hold = Mathf.Max(line.Duration, voice + 0.3f);
            return line;
        }

        /// <summary>Caption + voice + talking animation. Waits until the line has been said.</summary>
        IEnumerator Say(string id, float extraHold = 0f)
        {
            var line = Timed(id, out float voice);
            float dur = Captions.Show(line);
            if (Audio) Audio.Say(line, MagicianHead);
            Magician.Talking = true;
            float talk = voice > 0f ? voice : Mathf.Min(dur, 1.4f);
            yield return new WaitForSeconds(talk);
            Magician.Talking = false;
            float rest = voice > 0f ? 0.25f : Mathf.Max(0f, dur - talk) * 0.55f;
            yield return new WaitForSeconds(rest + extraHold);
        }

        /// <summary>Caption + voice without waiting (the caller carries on while it shows).</summary>
        void SayNow(string id)
        {
            var line = Timed(id, out float voice);
            Captions.Show(line);
            if (Audio) Audio.Say(line, MagicianHead);
            StartCoroutine(TalkFor(voice > 0f ? voice : Mathf.Min(line.Duration, 1.4f)));
        }

        IEnumerator TalkFor(float s) { Magician.Talking = true; yield return new WaitForSeconds(s); Magician.Talking = false; }

        void Sfx(string name, Vector3 worldPos, float volume = 1f, float pitch = 1f) { if (Audio) Audio.Play(name, worldPos, volume, pitch); }

        bool AnyPinch() => Hands.Get(Handed.Left).IsPinching || Hands.Get(Handed.Right).IsPinching;

        /// <summary>
        /// Runs one gate to completion. enter/stay are the strict and loose pose tests. onHint runs once at the
        /// hint time (the magician demonstrates). When hands are lost the gate's clock stops and, after a moment,
        /// the user is asked (in a caption and a gesture, not only in sound) to bring them back.
        /// </summary>
        IEnumerator RunGate(string name, BeatGate gate, Func<bool> enter, Func<bool> stay, Action onHint, bool needsHands = true, Action onAlt = null)
        {
            ActiveGate = gate; ActiveGateName = name;
            float opened = Time.time;
            while (!gate.Done)
            {
                bool input = !needsHands || Hands.AnyTracked();
                if (needsHands)
                {
                    _handsLostFor = input ? 0f : _handsLostFor + Time.deltaTime;
                    if (_handsLostFor > 2.5f && Time.time - _lastHandsNag > 9f)
                    {
                        _lastHandsNag = Time.time;
                        SayNow("hint.hands"); Magician.ShowHands();
                    }
                }
                bool e = input && enter();
                bool s = e || (input && stay());
                var ev = gate.Tick(Time.deltaTime, e, s, needsHands && AnyPinch(), input || Autopilot);
                if (ev == GateEvent.Hint) { Note("gate " + name + " hint"); onHint?.Invoke(); }
                else if (ev == GateEvent.AltOffered) { Note("gate " + name + " alt offered"); if (onAlt != null) onAlt(); else SayNow("hint.pinch"); }
                if (Autopilot && !gate.Done && Time.time - opened >= AutopilotDelay) gate.Complete();
                if (!gate.Done) yield return null;
            }
            Note("gate " + name + " → " + gate.Result);
            ActiveGate = null; ActiveGateName = "";
        }

        BeatGate NewGate(float hold = 0.4f) => new BeatGate(ShowGraph.Get(Model.State), hold);

        Rect Table => Site.TableRect;

        /// <summary>Keeps a stage-space point on the table, at least margin inside its edge.</summary>
        Vector3 ClampToTable(Vector3 p, float margin)
        {
            Rect r = Table;
            float mx = Mathf.Min(margin, r.width * 0.45f), mz = Mathf.Min(margin, r.height * 0.45f);
            return new Vector3(Mathf.Clamp(p.x, r.xMin + mx, r.xMax - mx), p.y, Mathf.Clamp(p.z, r.yMin + mz, r.yMax - mz));
        }

        /// <summary>Where the trapdoor can open so the whole opening stays on the table and contains the point.</summary>
        Vector3 PortalCentreFor(Vector3 stagePoint)
        {
            Vector2 h = Portal.FullHalfSize + new Vector2(0.02f, 0.02f);
            Rect r = Table;
            float x = r.width > 2 * h.x ? Mathf.Clamp(stagePoint.x, r.xMin + h.x, r.xMax - h.x) : r.center.x;
            float z = r.height > 2 * h.y ? Mathf.Clamp(stagePoint.z, r.yMin + h.y, r.yMax - h.y) : r.center.y;
            return new Vector3(x, 0, z);
        }

        Vector3 CoinRest(Vector3 stagePoint) => new Vector3(stagePoint.x, CoinProp.Thickness * 0.5f + 0.0005f, stagePoint.z);

        /// <summary>Scripted coin flight in stage space: an arc with tumble, ending flat.</summary>
        IEnumerator FlyCoin(Vector3 toStage, float seconds, float arc, float tumbles, bool flatAtEnd = true)
        {
            Coin.transform.SetParent(Stage, true);
            Vector3 from = Coin.transform.localPosition;
            Quaternion r0 = Coin.transform.localRotation;
            for (float e = 0; e < seconds; e += Time.deltaTime)
            {
                float x = e / seconds;
                Coin.transform.localPosition = Vector3.Lerp(from, toStage, x) + Vector3.up * (arc * 4f * x * (1f - x));
                Coin.transform.localRotation = Quaternion.Euler(x * tumbles * 360f, 0, 0) * r0;
                yield return null;
            }
            Coin.transform.localPosition = toStage;
            if (flatAtEnd) Coin.transform.localRotation = Quaternion.identity;
        }

        IEnumerator CoinLandBounce()
        {
            Sfx("coin_land", Coin.transform.position, 0.9f);
            Vector3 p = Coin.transform.localPosition;
            for (float e = 0; e < 0.22f; e += Time.deltaTime)
            {
                float x = e / 0.22f;
                Coin.transform.localPosition = p + Vector3.up * (0.012f * 4f * x * (1f - x));
                Coin.transform.localRotation = Quaternion.Euler(Mathf.Sin(x * Mathf.PI * 3f) * 14f * (1f - x), 0, 0);
                yield return null;
            }
            Coin.transform.localPosition = p; Coin.transform.localRotation = Quaternion.identity;
        }

        // ------------------------------------------------------------------ beats

        IEnumerator Placement()
        {
            if (PlaceStage != null) yield return PlaceStage();
            Note("placed: tier " + Site.Tier + (Site.Restored ? " (restored)" : "") + " table " + Site.TableRect);
        }

        // 0:00–0:30. No text walls: the onboarding is the act.
        IEnumerator Arrival()
        {
            float t0 = Time.realtimeSinceStartup;
            bool returning = Model.Returning || Site.Restored && NightStore.NightsCompleted > 0;
            Vector3 under = Portal.transform.TransformPoint(0, -0.02f, 0);

            if (returning)
            {
                // Before any input: three knocks from under the table, right where the stage was saved.
                Magician.KnockUp();
                for (int i = 0; i < 3; i++)
                {
                    Sfx("knock", under, 1f, 1f - i * 0.03f);
                    Sparkles.Burst(_sparkle, Portal.transform.TransformPoint(0, 0.004f, 0), 6, 0.05f);
                    yield return new WaitForSeconds(0.38f);
                }
                yield return new WaitForSeconds(0.35f);
            }
            else
            {
                Portal.SetSpot(1f);
                Sfx("whoosh", Portal.transform.position, 0.5f, 0.8f);
                yield return new WaitForSeconds(0.9f);
            }

            Sfx("trap_creak", under, 0.8f);
            yield return Portal.AnimateOpen(1f, 0.9f);
            Sfx("trap_clack", under, 0.9f);
            Sparkles.Trail(_dust, true, 5f);
            Magician.SetVisible(true);
            if (StartupSeconds < 0f) { StartupSeconds = Time.realtimeSinceStartup; Note("magician visible at " + StartupSeconds.ToString("0.00") + " s after launch"); }
            yield return Portal.AnimateLift(LiftTop, 1.6f);

            Magician.LookTarget = null;
            yield return new WaitForSeconds(Magician.Bow() * 0.75f);
            if (returning)
            {
                // He kept last night's card and shows it.
                int last = NightStore.LastCard;
                if (last >= 0) { EnsureBigCard(last, 0.55f); HoldCardInHand(); Magician.Present(); }
                yield return Say("return.hello");
                if (_bigCard) _bigCard.gameObject.SetActive(false);
            }
            else yield return Say("arrive.hello");

            Magician.ShowHands();
            SayNow("arrive.hands");

            // Both hands, or one hand held steadily: one-handed mode switches itself on.
            float oneHandTime = 0f;
            var gate = NewGate(0.35f);
            yield return RunGate("arrival.hands", gate,
                () =>
                {
                    if (Hands.BothTracked()) { return true; }
                    oneHandTime = Hands.AnyTracked() ? oneHandTime + Time.deltaTime : 0f;
                    return oneHandTime > 2.2f;
                },
                () => Hands.AnyTracked(),
                () => { Magician.ShowHands(); SayNow("arrive.hands"); },
                needsHands: false);
            if (!Hands.BothTracked() && Hands.AnyTracked()) { OneHanded = true; NightStore.OneHanded = true; Note("one-handed mode on"); }

            Magician.Celebrate();
            Sfx("shimmer", MagicianHead, 0.6f);
            yield return Say("arrive.begin");
            Note("arrival took " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
        }

        // "Think of a card": invisible head-gaze dwell, with a poke fallback when the reading is too close to call.
        IEnumerator GazeCardPick()
        {
            Case.SetOpen(true);
            Sfx("trap_clack", Case.transform.position, 0.35f, 1.6f);
            yield return new WaitForSeconds(0.35f);

            Vector3[] slots = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                slots[i] = Layout.CardRowCentre + new Vector3((i - 1) * Layout.CardSpacing, 0, 0);
                var c = _cards[i];
                c.gameObject.SetActive(true);
                c.transform.localPosition = Stage.InverseTransformPoint(Case.Mouth);
                c.transform.localRotation = Quaternion.Euler(80, 0, 0);
                c.transform.localScale = Vector3.one * 0.3f;
            }
            // rise out of the case one after another, face towards the user, tilted back to meet the gaze
            Quaternion facing = Quaternion.Euler(-18, 0, 0);
            for (int i = 0; i < 3; i++)
            {
                Sfx("card_flip", _cards[i].transform.position, 0.7f, 1f + i * 0.08f);
                StartCoroutine(Tween.Move(_cards[i].transform, slots[i], 0.75f, 0.05f));
                StartCoroutine(Tween.Rotate(_cards[i].transform, facing, 0.75f));
                StartCoroutine(Tween.Scale(_cards[i].transform, Vector3.one, 0.75f));
                yield return new WaitForSeconds(0.16f);
            }
            Magician.LookTarget = Stage.TransformPoint(Layout.CardRowCentre);
            yield return new WaitForSeconds(0.7f);
            Magician.Point(new Vector3(0, 0.8f, 0.5f));
            yield return Say("gaze.rule");

            // The measurement. Nothing is drawn: no cursor, no highlight.
            var picker = new GazeDwellPicker(3);
            Vector3[] world = new Vector3[3];
            SayNow("gaze.remember");
            Magician.LookTarget = null;
            for (float t = 0; t < 4.0f; t += Time.deltaTime)
            {
                for (int i = 0; i < 3; i++) world[i] = _cards[i].transform.position;
                picker.Accumulate(Head.Head, world, Stage.up, Time.deltaTime);
                // the cards breathe a little so the wait feels alive
                for (int i = 0; i < 3; i++) _cards[i].transform.localPosition = slots[i] + Vector3.up * (Mathf.Sin(Time.time * 1.7f + i) * 0.004f);
                yield return null;
            }
            int pick = picker.Pick(out bool ambiguous);
            Note("gaze dwell " + picker.Dwell[0].ToString("0.00") + "/" + picker.Dwell[1].ToString("0.00") + "/" + picker.Dwell[2].ToString("0.00") + " → " + pick + (ambiguous ? " (ambiguous)" : ""));

            if (ambiguous)
            {
                Magician.Ponder();
                yield return Say("gaze.poke");
                int poked = -1;
                Func<int> nearest = () =>
                {
                    for (int h = 0; h < 2; h++)
                    {
                        var hand = Hands.Get((Handed)h);
                        if (!hand.Tracked) continue;
                        for (int i = 0; i < 3; i++)
                            if ((hand.IndexTip - _cards[i].transform.position).magnitude < CardProp.PokeRadius) return i;
                    }
                    return -1;
                };
                var gate = NewGate(0.05f);
                yield return RunGate("gaze.poke", gate,
                    () => { int n = nearest(); if (n >= 0) poked = n; return n >= 0; },
                    () => false,
                    () => { Magician.Point(new Vector3(0, 0.8f, 0.5f)); SayNow("gaze.poke"); });
                if (poked >= 0) { pick = poked; Sfx("poke_click", _cards[pick].transform.position); }
                else if (gate.Result == GateEvent.AltSuccess)
                {
                    // pinch alternative: the card nearest the pinching hand
                    Handed which = Hands.Get(Handed.Right).IsPinching ? Handed.Right : Handed.Left;
                    Vector3 pp = Hands.Get(which).PinchPoint; float best = float.MaxValue;
                    for (int i = 0; i < 3; i++) { float d = (pp - _cards[i].transform.position).sqrMagnitude; if (d < best) { best = d; pick = i; } }
                }
            }
            Model.ChosenCard = pick;
            Note("chosen card slot " + pick + " (face " + Night.Cards[pick] + ")");

            Magician.Celebrate();
            SayNow("gaze.got");
            // shuffle: the cards swap places, then sink back into the case
            for (int s = 0; s < 3; s++)
            {
                int a = s % 3, b = (s + 1) % 3;
                Sfx("card_flip", _cards[a].transform.position, 0.6f, 1.1f);
                Vector3 pa = _cards[a].transform.localPosition, pb = _cards[b].transform.localPosition;
                StartCoroutine(Tween.Move(_cards[a].transform, pb, 0.28f, 0.03f));
                yield return Tween.Move(_cards[b].transform, pa, 0.28f, -0.03f);
            }
            Vector3 mouth = Stage.InverseTransformPoint(Case.Mouth);
            for (int i = 0; i < 3; i++)
            {
                StartCoroutine(Tween.Move(_cards[i].transform, mouth, 0.5f, 0.04f));
                StartCoroutine(Tween.Rotate(_cards[i].transform, Quaternion.Euler(80, 0, 0), 0.5f));
                StartCoroutine(Tween.Scale(_cards[i].transform, Vector3.one * 0.3f, 0.5f));
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.5f);
            foreach (var c in _cards) c.gameObject.SetActive(false);
            Case.SetOpen(false);
            Sfx("trap_clack", Case.transform.position, 0.4f, 1.5f);
            yield return new WaitForSeconds(0.6f);
        }

        // The hero: a coin resting on the real table falls through it.
        IEnumerator BorrowedCoin()
        {
            Magician.LookTarget = Head.Head.position;
            GiveCoinToMagician();
            Magician.Present();
            yield return Say("coin.lend");

            // he flips it onto the table in front of the user
            Vector3 landing = CoinRest(ClampToTable(Layout.CoinLanding, 0.05f));
            Sfx("whoosh", Coin.transform.position, 0.35f, 1.5f);
            yield return FlyCoin(landing, 0.75f, 0.16f, 3f);
            yield return CoinLandBounce();
            Magician.Idle();

            // --- pick it up -------------------------------------------------------------
            SayNow("coin.pick");
            Handed holder = Handed.Right; bool holding = false; bool ticked = false;
            var pick = NewGate(0.05f);
            yield return RunGate("coin.pick", pick,
                () =>
                {
                    float nearest = float.MaxValue;
                    for (int h = 0; h < 2; h++)
                    {
                        var hand = Hands.Get((Handed)h);
                        if (!hand.Tracked) continue;
                        float dPinch = (hand.PinchPoint - Coin.transform.position).magnitude;
                        float dPalm = (hand.PalmPosition - Coin.transform.position).magnitude;
                        nearest = Mathf.Min(nearest, Mathf.Min(dPinch, dPalm));
                        // pinch grab, or a whole-hand palm grab: both count, with a generous volume
                        if ((hand.IsPinching && dPinch < CoinProp.GrabRadius) || (hand.Curl > 0.6f && dPalm < CoinProp.GrabRadius * 1.25f))
                        { holder = (Handed)h; holding = true; return true; }
                    }
                    bool near = nearest < 0.13f;
                    Coin.SetGlow(near ? 1f : 0.25f);
                    if (near && !ticked) { ticked = true; Sfx("tick", Coin.transform.position, 0.5f); }
                    if (!near) ticked = false;
                    return false;
                },
                () => false,
                () => { Magician.Point(Stage.InverseTransformPoint(Coin.transform.position) - Magician.transform.parent.localPosition); SayNow("coin.pick"); });

            if (!holding && pick.Result == GateEvent.AltSuccess)
            {
                // pinch anywhere: the coin jumps to the pinching hand
                holder = Hands.Get(Handed.Right).IsPinching ? Handed.Right : Handed.Left;
                holding = true;
            }

            if (holding)
            {
                Sfx("coin_chime", Coin.transform.position, 0.6f, 1.2f);
                Coin.SetGlow(0.6f);
                Coin.transform.SetParent(Stage, true);

                // --- carry it, then set it down anywhere ------------------------------------
                SayNow("coin.place");
                float heldFor = 0f; bool released = false;
                var place = NewGate(0.12f);
                yield return RunGate("coin.place", place,
                    () =>
                    {
                        var hand = Hands.Get(holder);
                        if (!hand.Tracked) return false;                       // tracking lost: the coin waits in the air
                        bool gripping = hand.Pinch > 0.45f || hand.Curl > 0.5f; // looser than the grab threshold (hysteresis)
                        if (gripping)
                        {
                            heldFor += Time.deltaTime;
                            Vector3 target = hand.Curl > 0.5f && hand.Pinch < 0.45f ? hand.PalmPosition : hand.PinchPoint;
                            Coin.transform.position = Vector3.Lerp(Coin.transform.position, target, 1f - Mathf.Exp(-22f * Time.deltaTime));
                            Coin.transform.Rotate(Vector3.right, 140f * Time.deltaTime, Space.Self);
                            return false;
                        }
                        released = heldFor > 0.35f;                            // must actually have carried it
                        return released;
                    },
                    () => !(Hands.Get(holder).Pinch > 0.45f || Hands.Get(holder).Curl > 0.5f) && heldFor > 0.35f,
                    () => { Magician.Point(new Vector3(-0.3f, -0.2f, -1f)); SayNow("coin.place"); },
                    needsHands: true,
                    onAlt: () => SayNow("coin.place"));
                Coin.SetGlow(0f);
                // wherever it was let go, it comes to rest on the table directly below (or the nearest table point)
                Vector3 drop = CoinRest(ClampToTable(Stage.InverseTransformPoint(Coin.transform.position), 0.04f));
                yield return FlyCoin(drop, Mathf.Clamp(Mathf.Abs(Stage.InverseTransformPoint(Coin.transform.position).y) * 2.2f, 0.12f, 0.45f), released ? 0f : 0.05f, 1f);
                yield return CoinLandBounce();
            }
            else
            {
                Coin.SetGlow(0f);    // timed out: the trick proceeds with the coin where he flipped it
            }

            // --- night 3: the user chooses where the door opens --------------------------
            Vector3 coinAt = Coin.transform.localPosition;
            Vector3 door = PortalCentreFor(coinAt);
            if (Night.Trapdoor == TrapdoorSpot.WhereTheUserTaps)
            {
                Vector3 tapped = door; bool got = false;
                var tap = NewGate(0.05f);
                yield return RunGate("coin.tap", tap,
                    () =>
                    {
                        for (int h = 0; h < 2; h++)
                        {
                            var hand = Hands.Get((Handed)h);
                            if (!hand.Tracked) continue;
                            Vector3 tip = Stage.InverseTransformPoint(hand.IndexTip);
                            if (Mathf.Abs(tip.y) < 0.025f && Table.Contains(new Vector2(tip.x, tip.z))) { tapped = tip; got = true; return true; }
                        }
                        return false;
                    },
                    () => false,
                    () => { Magician.Point(new Vector3(0.4f, -0.2f, -1f)); SayNow("coin.place"); });
                if (got) { door = PortalCentreFor(tapped); Sfx("poke_click", Stage.TransformPoint(tapped)); Sparkles.Burst(_sparkle, Stage.TransformPoint(tapped), 14, 0.08f); }
            }

            // --- the tap ----------------------------------------------------------------
            Magician.LookTarget = Stage.TransformPoint(coinAt);
            SayNow("coin.watch");
            yield return new WaitForSeconds(Magician.WandTap());
            Sfx("wand_tap", Magician.WandTip.position);
            Sparkles.Burst(_sparkle, Magician.WandTip.position, 26, 0.16f);
            yield return new WaitForSeconds(0.35f);

            // he drops out of sight and the table closes over him: the table is whole again
            Magician.LookTarget = null;
            StartCoroutine(Portal.AnimateLift(LiftBottom, 0.75f));
            yield return new WaitForSeconds(0.3f);
            yield return Portal.AnimateOpen(0f, 0.45f);
            Sfx("trap_clack", Portal.transform.position, 0.8f);
            Sparkles.Trail(_dust, false);
            Portal.SetSpot(0f);
            yield return new WaitForSeconds(0.5f);

            // the trapdoor moves, unseen, to wherever the coin is
            Portal.transform.localPosition = door;
            if (Audio) Audio.MoveMusic(Portal.transform.TransformPoint(0, -0.5f, 0));
            Vector3 over = CoinRest(door + Vector3.ClampMagnitude(new Vector3(coinAt.x - door.x, 0, coinAt.z - door.z), Mathf.Min(Portal.FullHalfSize.x, Portal.FullHalfSize.y) - 0.035f));
            // sparks race around the rectangle that is about to open
            Vector2 hs = Portal.FullHalfSize;
            for (int i = 0; i <= 16; i++)
            {
                float a = i / 16f * 4f; int side = Mathf.Min(3, (int)a); float f = a - side;
                Vector3 p = side == 0 ? new Vector3(Mathf.Lerp(-hs.x, hs.x, f), 0, hs.y) : side == 1 ? new Vector3(hs.x, 0, Mathf.Lerp(hs.y, -hs.y, f))
                          : side == 2 ? new Vector3(Mathf.Lerp(hs.x, -hs.x, f), 0, -hs.y) : new Vector3(-hs.x, 0, Mathf.Lerp(-hs.y, hs.y, f));
                Sparkles.Burst(_sparkle, Portal.transform.TransformPoint(p + Vector3.up * 0.004f), 3, 0.03f);
                if (i % 4 == 0) Sfx("tick", Portal.transform.TransformPoint(p), 0.6f, 1.3f);
                yield return new WaitForSeconds(0.035f);
            }
            if ((Coin.transform.localPosition - over).magnitude > 0.01f)
            {
                // the coin was not over the door (night 3, or right at a table edge): it hops there
                yield return FlyCoin(over, 0.4f, 0.07f, 1f);
                yield return CoinLandBounce();
            }

            // --- it opens, and the coin goes through the table -----------------------------
            Magician.ReachUp();
            Portal.SetSpot(0.5f);
            Sfx("trap_creak", Portal.transform.position, 0.7f, 1.25f);
            StartCoroutine(Portal.AnimateOpen(1f, 0.5f));
            yield return new WaitForSeconds(0.2f);       // a held breath: the coin hangs over nothing
            Sfx("trap_clack", Portal.transform.position, 1f);

            Coin.transform.SetParent(Portal.transform, true);
            Vector3 start = Coin.transform.localPosition;
            Vector3 hand = Portal.transform.InverseTransformPoint(Magician.RightHand.position) + Vector3.up * 0.012f;
            Sparkles.Trail(_sparkle, true, 70f);
            if (Audio) Audio.PlayFollowing("coin_chime", Coin.transform, 0.9f);
            const float fall = 1.35f;
            for (float e = 0; e < fall; e += Time.deltaTime)
            {
                float x = e / fall;
                // hand-authored: accelerating drop, drifting to his hand, tumbling end over end. Not free physics,
                // so he always catches it.
                float down = x * x;
                Vector3 p = new Vector3(Mathf.Lerp(start.x, hand.x, Mathf.SmoothStep(0, 1, x)), Mathf.Lerp(start.y, hand.y, down), Mathf.Lerp(start.z, hand.z, Mathf.SmoothStep(0, 1, x)));
                p += new Vector3(Mathf.Sin(x * 9f), 0, Mathf.Cos(x * 7f)) * (0.012f * (1f - x));
                Coin.transform.localPosition = p;
                Coin.transform.localRotation = Quaternion.Euler(x * 1500f, x * 320f, 0);
                _sparkle.transform.position = Coin.transform.position;
                if (Audio) Audio.SetFollowDepth(down);
                yield return null;
            }
            Sparkles.Trail(_sparkle, false);
            GiveCoinToMagician();
            Sfx("coin_chime_low", Coin.transform.position, 1f);
            Sparkles.Burst(_sparkle, Coin.transform.position, 30, 0.2f);
            Magician.Celebrate();
            Sfx("applause_small", Portal.transform.TransformPoint(0, -0.6f, 0.2f), 0.9f);
            StartCoroutine(AudienceBounce(3.5f));
            yield return Say("coin.caught");

            // --- "Have a look down here": the door stays open so the user can lean in --------
            Magician.LookTarget = Head.Head.position;
            Magician.Wave();
            SayNow("coin.lean");
            bool waved = false;
            for (float t = 0; t < 4.5f; t += Time.deltaTime)
            {
                Magician.LookTarget = Head.Head.position;
                Vector3 headLocal = Portal.transform.InverseTransformPoint(Head.Head.position);
                if (!waved && new Vector2(headLocal.x, headLocal.z).magnitude < 0.26f) { waved = true; Magician.Celebrate(); Sfx("applause_small", Portal.transform.TransformPoint(0, -0.6f, 0.2f), 0.6f); }
                yield return null;
            }

            // --- back home ------------------------------------------------------------------
            yield return Portal.AnimateOpen(0f, 0.45f);
            Sfx("trap_clack", Portal.transform.position, 0.8f);
            yield return new WaitForSeconds(0.35f);
            Portal.transform.localPosition = _portalHome;
            if (Audio) Audio.MoveMusic(Portal.transform.TransformPoint(0, -0.5f, 0));
            Portal.SetSpot(0.6f);
            Sfx("trap_creak", Portal.transform.position, 0.6f);
            yield return Portal.AnimateOpen(1f, 0.6f);
            Sparkles.Trail(_dust, true, 5f);
            yield return Portal.AnimateLift(LiftTop, 1.3f);
        }

        // The coin reappears in the user's open palm and rides it.
        IEnumerator PalmCoin()
        {
            Magician.LookTarget = Head.Head.position;
            Magician.ShowHands();
            SayNow("palm.offer");
            Handed which = Handed.Right; HandSnapshot found = default;
            var gate = NewGate(0.4f);
            yield return RunGate("palm.offer", gate,
                () => Hands.TryFind(h => PoseMath.PalmUpOpen(h), out found, out which),
                () => Hands.TryFind(h => PoseMath.PalmUpOpen(h, true), out found, out which),
                () => { Magician.ShowHands(); SayNow("palm.offer"); });

            bool inPalm = gate.Result == GateEvent.Success;
            if (gate.Result == GateEvent.AltSuccess)
            {
                which = Hands.Get(Handed.Right).IsPinching ? Handed.Right : Handed.Left;
                inPalm = true;
            }
            if (!inPalm) { yield return new WaitForSeconds(0.2f); yield break; }   // timed out: move on, never block

            // vanish from his hand, shimmer, appear in the palm
            Sparkles.Burst(_sparkle, Coin.transform.position, 18, 0.12f);
            Coin.SetVisible(false);
            Magician.Present();
            Vector3 palm = PalmPoint(Hands.Get(which));
            Sfx("shimmer", palm, 0.8f);
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { Sparkles.Burst(_sparkle, PalmPoint(Hands.Get(which)), 2, 0.05f); yield return null; }

            bool bird = Night.Palm == PalmVariant.Bird && Bird;
            Coin.transform.SetParent(Stage, false);
            Coin.transform.position = PalmPoint(Hands.Get(which));
            Coin.transform.localRotation = Quaternion.identity;
            if (bird) { Bird.Show(Coin.transform.position); Sfx("bird", Coin.transform.position, 0.9f); }
            else { Coin.SetVisible(true); Sfx("coin_chime", Coin.transform.position, 0.7f); }
            SayNow("palm.there");

            // it stays anchored to the hand as the hand moves
            for (float t = 0; t < 3.6f; t += Time.deltaTime)
            {
                var h = Hands.Get(which);
                if (h.Tracked)
                {
                    Vector3 p = PalmPoint(h);
                    Coin.transform.position = Vector3.Lerp(Coin.transform.position, p, 1f - Mathf.Exp(-25f * Time.deltaTime));
                    Coin.transform.rotation = Quaternion.Slerp(Coin.transform.rotation, Quaternion.LookRotation(h.PalmForward.sqrMagnitude > 0.01f ? h.PalmForward : Vector3.forward, h.PalmNormal), 1f - Mathf.Exp(-18f * Time.deltaTime));
                    if (bird) Bird.Follow(p + h.PalmNormal * 0.012f, t);
                }
                yield return null;
            }

            // ...then it hops back to him
            if (bird) { Bird.Hide(); Sparkles.Burst(_sparkle, Coin.transform.position, 20, 0.14f); Coin.SetVisible(true); Sfx("bird", Coin.transform.position, 0.7f, 1.2f); }
            Magician.ReachUp();
            Sfx("whoosh", Coin.transform.position, 0.4f, 1.4f);
            Vector3 handPos = Stage.InverseTransformPoint(Magician.RightHand.position);
            yield return FlyCoin(handPos, 0.6f, 0.12f, 2f, false);
            GiveCoinToMagician();
            Sfx("coin_chime", Coin.transform.position, 0.6f, 1.1f);
            Magician.Present();
            yield return new WaitForSeconds(0.5f);
        }

        static Vector3 PalmPoint(in HandSnapshot h) => h.PalmPosition + h.PalmNormal * 0.016f;

        // Role reversal: he teaches one move and the user performs it.
        IEnumerator Lesson()
        {
            bool reappear = Night.Lesson == LessonVariant.Reappear;
            Magician.LookTarget = Head.Head.position;
            yield return Say("lesson.intro");

            // he demonstrates first, with his own tiny hands
            float demo = Magician.DemonstrateVanish();
            SayNow("lesson.demo");
            yield return new WaitForSeconds(0.7f);
            if (!reappear) { Coin.SetVisible(false); Sparkles.Burst(_sparkle, Magician.RightHand.position, 10, 0.08f); }
            yield return new WaitForSeconds(0.8f);
            if (reappear) { GiveCoinToMagician(); Sparkles.Burst(_sparkle, Magician.RightHand.position, 10, 0.08f); }
            yield return new WaitForSeconds(demo - 1.5f);
            GiveCoinToMagician();

            Handed which = Handed.Right; HandSnapshot found = default;
            bool handed = false;

            if (!reappear)
            {
                // the coin goes to the user's open palm
                Magician.ShowHands();
                SayNow("palm.offer");
                var offer = new BeatGate(4f, 9f, 14f, 0.35f);
                yield return RunGate("lesson.palm", offer,
                    () => Hands.TryFind(h => PoseMath.PalmUpOpen(h), out found, out which),
                    () => Hands.TryFind(h => PoseMath.PalmUpOpen(h, true), out found, out which),
                    () => { Magician.ShowHands(); SayNow("palm.offer"); });
                handed = offer.Result == GateEvent.Success || offer.Result == GateEvent.AltSuccess;
                if (offer.Result == GateEvent.AltSuccess) which = Hands.Get(Handed.Right).IsPinching ? Handed.Right : Handed.Left;
                if (handed)
                {
                    Sfx("whoosh", Coin.transform.position, 0.4f, 1.4f);
                    yield return FlyCoin(Stage.InverseTransformPoint(PalmPoint(Hands.Get(which))), 0.55f, 0.12f, 2f);
                    Sfx("coin_chime", Coin.transform.position, 0.6f);
                }
            }
            else
            {
                // the Reappear: the coin goes down into the pit first
                Sparkles.Burst(_sparkle, Coin.transform.position, 16, 0.1f);
                Coin.SetVisible(false);
                handed = Hands.AnyTracked();
                if (handed) Hands.TryFind(h => true, out found, out which);
            }

            // close...
            SayNow("lesson.close");
            var close = new BeatGate(ShowGraph.Hint, ShowGraph.Alt, ShowGraph.Auto, 0.25f);
            yield return RunGate("lesson.close", close,
                () =>
                {
                    FollowPalm(which, handed && !reappear);
                    if (Hands.TryFind(h => PoseMath.Fist(h), out found, out var w)) { which = w; return true; }
                    return false;
                },
                () => PoseMath.Fist(Hands.Get(which), true),
                () => { Magician.DemonstrateVanish(); SayNow("lesson.close"); });
            if (!reappear)
            {
                Sparkles.Burst(_sparkle, Coin.transform.position, 12, 0.06f);
                Coin.SetVisible(false);
            }
            Sfx("tick", Hands.Get(which).Tracked ? Hands.Get(which).PalmPosition : Coin.transform.position, 0.7f);

            // ...and open
            SayNow("lesson.open");
            var open = new BeatGate(ShowGraph.Hint, ShowGraph.Alt, ShowGraph.Auto, 0.2f);
            yield return RunGate("lesson.open", open,
                () => PoseMath.Open(Hands.Get(which)),
                () => PoseMath.Open(Hands.Get(which), true),
                () => { Magician.DemonstrateVanish(); SayNow("lesson.open"); });

            Vector3 at = Hands.Get(which).Tracked ? PalmPoint(Hands.Get(which)) : Stage.TransformPoint(Layout.CoinLanding + Vector3.up * 0.12f);
            Sfx("shimmer", at, 0.9f);
            Sparkles.Burst(_sparkle, at, 34, 0.2f);
            if (reappear)
            {
                // it is back, in the user's own hand
                Coin.transform.SetParent(Stage, false);
                Coin.transform.position = at; Coin.transform.localRotation = Quaternion.identity;
                Coin.SetVisible(true);
                Sfx("coin_chime", at, 0.8f);
            }
            Magician.Celebrate();
            Sfx("applause_small", Portal.transform.TransformPoint(0, -0.5f, 0.2f), 0.8f);
            StartCoroutine(AudienceBounce(3f));
            if (reappear)
            {
                for (float t = 0; t < 1.6f; t += Time.deltaTime) { FollowPalm(which, true); yield return null; }
            }
            yield return Say("lesson.bravo");

            // the coin turns up with him again
            if (reappear) { Sfx("whoosh", Coin.transform.position, 0.4f, 1.4f); yield return FlyCoin(Stage.InverseTransformPoint(Magician.RightHand.position), 0.55f, 0.1f, 2f, false); }
            GiveCoinToMagician();
            Sparkles.Burst(_sparkle, Coin.transform.position, 14, 0.1f);
            Magician.Present();
            yield return Say("lesson.friend");
            // he pockets the coin for the finale
            Sparkles.Burst(_sparkle, Coin.transform.position, 8, 0.06f);
            Coin.SetVisible(false);
            Magician.Idle();
        }

        void FollowPalm(Handed which, bool active)
        {
            if (!active || !Coin.IsVisible) return;
            var h = Hands.Get(which);
            if (!h.Tracked) return;
            Coin.transform.position = Vector3.Lerp(Coin.transform.position, PalmPoint(h), 1f - Mathf.Exp(-25f * Time.deltaTime));
            Coin.transform.rotation = Quaternion.Slerp(Coin.transform.rotation, Quaternion.LookRotation(h.PalmForward.sqrMagnitude > 0.01f ? h.PalmForward : Vector3.forward, h.PalmNormal), 1f - Mathf.Exp(-18f * Time.deltaTime));
        }

        // "Your Card": the case is empty, and the card rises from the deep stage.
        IEnumerator CardFinale()
        {
            Magician.LookTarget = Stage.TransformPoint(Case.transform.localPosition);
            Magician.DemonstrateCover();
            SayNow("finale.cover");
            var cover = NewGate(0.45f);
            HandSnapshot found = default; Handed which = Handed.Right;
            Vector3 up = Stage.up;
            yield return RunGate("finale.cover", cover,
                () => Hands.TryFind(h => PoseMath.CoversPoint(h, Case.CoverPoint, up, VelvetCase.CoverRadius, VelvetCase.CoverHeight), out found, out which),
                () => Hands.TryFind(h => PoseMath.CoversPoint(h, Case.CoverPoint, up, VelvetCase.CoverRadius, VelvetCase.CoverHeight, true), out found, out which),
                () => { Magician.DemonstrateCover(); SayNow("finale.cover"); });
            bool covered = cover.Result == GateEvent.Success;
            Sfx("shimmer", Case.CoverPoint, 0.6f);
            Sparkles.Burst(_sparkle, Case.CoverPoint, 16, 0.1f);

            // while the hand hides the case, he slips away below
            Magician.LookTarget = null;
            StartCoroutine(Portal.AnimateLift(LiftBottom, 1.1f));

            if (covered)
            {
                SayNow("finale.lift");
                var lift = new BeatGate(4f, 8f, 12f, 0.2f);
                yield return RunGate("finale.lift", lift,
                    () => !Hands.TryFind(h => PoseMath.CoversPoint(h, Case.CoverPoint, up, VelvetCase.CoverRadius, VelvetCase.CoverHeight, true), out found, out which),
                    () => !Hands.TryFind(h => PoseMath.CoversPoint(h, Case.CoverPoint, up, VelvetCase.CoverRadius, VelvetCase.CoverHeight), out found, out which),
                    () => SayNow("finale.lift"),
                    needsHands: false);
            }
            else yield return new WaitForSeconds(0.8f);

            Case.SetOpen(true);
            Sfx("trap_clack", Case.transform.position, 0.5f, 1.5f);
            yield return new WaitForSeconds(0.5f);
            yield return Say("finale.empty");

            // the trapdoor opens wider
            Sfx("trap_creak", Portal.transform.position, 0.8f, 0.9f);
            yield return Portal.AnimateWiden(Portal.MaxWiden, 0.9f);
            Sfx("trap_clack", Portal.transform.position, 0.9f);

            int slot = Mathf.Clamp(Model.ChosenCard, 0, 2);
            int face = Night.Cards[slot];
            if (Night.Reveal == RevealVariant.Placards)
            {
                // night 2: the audience holds the card up on placards
                var mat = Portal.Theatre.Placards.GetComponent<Renderer>().sharedMaterial;
                CardProp.FaceUv(face, out var uvMin, out var uvMax);
                mat.SetTextureOffset("_MainTex", uvMin); mat.SetTextureScale("_MainTex", uvMax - uvMin);
                Portal.Theatre.Placards.gameObject.SetActive(true);
                Portal.Theatre.Placards.localScale = new Vector3(1, 0.01f, 1);
                Magician.Point(new Vector3(0, 0.6f, 0.8f));
                Sfx("card_flip", Portal.transform.TransformPoint(0, -0.5f, 0.3f), 1f, 0.8f);
                yield return Tween.Scale(Portal.Theatre.Placards, Vector3.one, 0.5f);
                Sfx("applause_small", Portal.transform.TransformPoint(0, -0.5f, 0.2f), 0.9f);
                StartCoroutine(AudienceBounce(3f));
                yield return new WaitForSeconds(0.6f);
            }
            // ...and the card itself rises from the deep stage on the lift, with him standing in front of it
            EnsureBigCard(face, 1.55f);
            _bigCard.transform.SetParent(Portal.Lift, false);
            _bigCard.transform.localPosition = new Vector3(0, CardProp.Height * 1.55f * 0.5f + 0.004f, 0.075f);
            _bigCard.transform.localRotation = Quaternion.Euler(-12, 0, 0);
            _bigCard.gameObject.SetActive(true);
            Magician.Present();
            Sfx("fanfare", Portal.transform.position, 0.9f);
            Sparkles.Trail(_sparkle, true, 40f);
            _sparkle.transform.position = Portal.transform.position;
            yield return Portal.AnimateLift(LiftTop, 2.3f);
            Sparkles.Trail(_sparkle, false);
            Magician.LookTarget = Head.Head.position;
            Sfx("applause_big", Portal.transform.TransformPoint(0, -0.4f, 0.2f), 0.9f);
            StartCoroutine(AudienceBounce(5f));
            yield return Say("finale.yours");
            Magician.Bow();
            yield return new WaitForSeconds(1.4f);
        }

        // "Tonight, the star is you": curtains over the user's real wall, and the audience applauds the user.
        IEnumerator CurtainCall()
        {
            Magician.LookTarget = Head.Head.position;
            yield return Say("curtain.star");
            if (Curtains) yield return Curtains.Show(Site, Head.Head);
            Sfx("applause_big", Curtains ? Curtains.Centre : Portal.transform.position, 1f);
            StartCoroutine(AudienceBounce(6f));
            Magician.Celebrate();

            // the user's bow: a head dip. Optional (cut list item 1), so its fuse is short.
            SayNow("curtain.bow");
            float baseline = PoseMath.PitchDownDegrees(Head.Head.rotation);
            var bow = new BeatGate(3f, 6f, 9f, 0.15f);
            yield return RunGate("curtain.bow", bow,
                () => PoseMath.PitchDownDegrees(Head.Head.rotation) - baseline > 14f,
                () => PoseMath.PitchDownDegrees(Head.Head.rotation) - baseline > 9f,
                () => { Magician.Bow(); SayNow("curtain.bow"); },
                needsHands: false);
            if (bow.Result == GateEvent.Success || bow.Result == GateEvent.AltSuccess)
            {
                Sfx("applause_big", Curtains ? Curtains.Centre : Portal.transform.position, 1f, 1.05f);
                Vector3 above = (Curtains ? Curtains.Centre : Stage.TransformPoint(0, 0.5f, 0.3f)) + Vector3.up * 0.25f;
                for (int i = 0; i < 6; i++) { Sparkles.Burst(_roses, above + UnityEngine.Random.insideUnitSphere * 0.3f, 6, 0.25f); yield return new WaitForSeconds(0.18f); }
                Magician.Celebrate();
            }
            Magician.Bow();
            yield return new WaitForSeconds(1.6f);

            if (Curtains) yield return Curtains.Hide();
            if (_bigCard) _bigCard.gameObject.SetActive(false);
            yield return Portal.AnimateWiden(1f, 0.6f);
            if (Title) { yield return Title.Show(Night.Title); yield return new WaitForSeconds(2.4f); yield return Title.Hide(); }
        }

        // The show is complete. Save the night, hint at tomorrow, and offer the encore.
        IEnumerator Encore()
        {
            int face = Night.Cards[Mathf.Clamp(Model.ChosenCard, 0, 2)];
            NightCompleted?.Invoke(Model.Night, face);
            Note("night " + Model.Night + " complete (runs: " + Model.Runs + ")");

            Magician.LookTarget = Head.Head.position;
            Magician.Wave();
            yield return Say("encore.tomorrow");
            Token.transform.localPosition = ClampToTable(Layout.Encore, 0.05f) + Vector3.up * Layout.Encore.y;
            Token.Shown = true;
            Sfx("shimmer", Token.transform.position, 0.6f);
            yield return new WaitForSeconds(0.6f);
            SayNow("encore.again");

            // Resting state: wait for a poke. A long pinch on the token is the hidden debug gesture.
            float pinchHeld = 0f, lastNag = Time.time;
            var gate = new BeatGate(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, 0.05f);
            yield return RunGate("encore.poke", gate,
                () =>
                {
                    Magician.LookTarget = Head.Head.position;
                    bool poke = false, pinchOn = false;
                    for (int h = 0; h < 2; h++)
                    {
                        var hand = Hands.Get((Handed)h);
                        if (!hand.Tracked) continue;
                        float d = (hand.IndexTip - Token.transform.position).magnitude;
                        if (hand.IsPinching && (hand.PinchPoint - Token.transform.position).magnitude < EncoreToken.PokeRadius * 1.3f) pinchOn = true;
                        else if (d < EncoreToken.PokeRadius) poke = true;
                    }
                    if (pinchOn)
                    {
                        pinchHeld += Time.deltaTime;
                        if (pinchHeld >= 3f) { pinchHeld = -1000f; Sfx("tick", Token.transform.position); DebugGesture?.Invoke(); }
                        return false;
                    }
                    if (pinchHeld < 0f || pinchHeld > 0.3f) { pinchHeld = 0f; return false; }   // a released pinch is not a poke
                    pinchHeld = 0f;
                    if (Time.time - lastNag > 25f) { lastNag = Time.time; Magician.Wave(); SayNow("encore.again"); }
                    return poke;
                },
                () => false, null, needsHands: false);

            Sfx("poke_click", Token.transform.position);
            Sparkles.Burst(_sparkle, Token.transform.position, 24, 0.18f);
            Token.Shown = false;
            // going round again: he rides the lift down and the table closes, ready for a clean Arrival
            Magician.LookTarget = null;
            Magician.Bow();
            yield return new WaitForSeconds(1.2f);
            yield return Portal.AnimateLift(LiftBottom, 1.0f);
            yield return Portal.AnimateOpen(0f, 0.5f);
            Sfx("trap_clack", Portal.transform.position, 0.8f);
            Model.ChosenCard = -1;
        }

        // ------------------------------------------------------------------ small helpers

        void EnsureBigCard(int face, float scale)
        {
            if (_bigCard && _bigCard.Face != face) { Destroy(_bigCard.gameObject); _bigCard = null; }
            if (!_bigCard) _bigCard = CardProp.Create(Stage, face);
            _bigCard.transform.localScale = Vector3.one * scale;
            _bigCard.gameObject.SetActive(true);
        }

        void HoldCardInHand()
        {
            _bigCard.transform.SetParent(Magician.RightHand, false);
            _bigCard.transform.localPosition = new Vector3(0, -0.035f, 0.012f);
            _bigCard.transform.localRotation = Quaternion.Euler(-70, 0, 0);
        }

        IEnumerator AudienceBounce(float seconds)
        {
            var a = Portal.Theatre.Audience;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float fade = Mathf.Clamp01((seconds - t) / 0.6f);
                a.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(t * 11f)) * 0.006f * fade);
                yield return null;
            }
            a.localPosition = Vector3.zero;
        }
    }
}
