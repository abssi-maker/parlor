using System;
using System.Collections;
using UnityEngine;

namespace Parlor
{
    /// <summary>Debug and test switches. In a release build only -parlorNight and -parlorTier are honoured.</summary>
    public sealed class ParlorOptions
    {
        public int Night;            // 0 = whatever night the player is on
        public int Tier;             // 0 = automatic, 3 = force the floating table
        public bool Autopilot;       // gates complete themselves (never in a release build)
        public string Beat = "";     // start at this beat
        public bool Mute;

        /// <summary>Editor-only: extra arguments stored in PlayerPrefs, so an automated Play-mode run can be configured.</summary>
        public const string EditorArgsKey = "parlor.debug.args";

        public static ParlorOptions FromEnvironment()
        {
            var o = new ParlorOptions();
            string[] args = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
            args = PlayerPrefs.GetString(EditorArgsKey, "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
#endif
            foreach (var a in args)
            {
                if (a.StartsWith("-parlorNight=")) int.TryParse(a.Substring(13), out o.Night);
                else if (a.StartsWith("-parlorTier=")) int.TryParse(a.Substring(12), out o.Tier);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                else if (a == "-parlorAutopilot") o.Autopilot = true;
                else if (a == "-parlorMute") o.Mute = true;
                else if (a.StartsWith("-parlorBeat=")) o.Beat = a.Substring(12);
#endif
            }
            return o;
        }
    }

    /// <summary>
    /// Builds the whole show from code and runs it: place the stage, build the trapdoor theatre, the magician and
    /// the props, then hand over to ShowDirector. There is no scene content to drift out of sync: the scene only
    /// holds the camera rig. Input and room data arrive through three interfaces, so the same app runs on a Quest,
    /// in Meta XR Simulator and in headless tests.
    /// </summary>
    public sealed class ParlorApp : MonoBehaviour
    {
        public static ParlorApp Instance { get; private set; }

        public IHandSource Hands { get; private set; }
        public IHeadSource Head { get; private set; }
        public IRoomSource Room { get; private set; }
        public ParlorOptions Options { get; private set; }
        public Transform Stage { get; private set; }
        public ShowDirector Director { get; private set; }
        public bool Ready { get; private set; }
        /// <summary>Called once the stage has a pose (the Quest layer locks it with a spatial anchor).</summary>
        public Action<Transform, StageSite> StagePlaced;
        /// <summary>Last session's stage pose from a saved spatial anchor, if one was found (Quest layer).</summary>
        /// <summary>Room brightness requested by the show, 0..1 (Quest layer: passthrough styling).</summary>
        public Action<float> HouseLights;
        public Func<Pose?> SavedStage;
        public Func<bool> SavedStageSettled;

        public static ParlorApp Launch(IHandSource hands, IHeadSource head, IRoomSource room, ParlorOptions options = null)
        {
            if (Instance) Destroy(Instance.gameObject);
            var go = new GameObject("PARLOR");
            var app = go.AddComponent<ParlorApp>();
            app.Hands = hands; app.Head = head; app.Room = room;
            app.Options = options ?? ParlorOptions.FromEnvironment();
            Instance = app;
            return app;
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            Stage = new GameObject("Stage").transform;
            Stage.SetParent(transform, false);
            var site = new StageSite();
            // give a saved anchor a moment to be found again (only when there is one to look for)
            if (SavedStageSettled != null && !string.IsNullOrEmpty(NightStore.AnchorUuid))
                while (!SavedStageSettled() && Time.realtimeSinceStartup - t0 < 2.5f) yield return null;
            yield return StagePlacer.Place(Stage, site, Room, Head, Options.Tier, SavedStage != null ? SavedStage() : null);
            StagePlaced?.Invoke(Stage, site);

            var portalGo = new GameObject("Portal");
            portalGo.transform.SetParent(Stage, false);
            var portal = portalGo.AddComponent<TablePortal>();
            Rect r = site.TableRect;
            // The shaft leans along the user's line of sight from where they sit now (clamped: a user standing right
            // over the table still gets a near-vertical shaft, a user far back does not get an absurd one).
            Vector3 eye = Stage.InverseTransformPoint(Head.Head.position);
            float lean = eye.y > 0.15f ? Mathf.Clamp(-eye.z / eye.y * 0.92f, 0.25f, 1.30f) : 1.0f;
            portal.Build(new Vector2(Mathf.Min(-r.xMin, r.xMax), Mathf.Min(-r.yMin, r.yMax)), site.Tier == 3, lean);

            Director = Stage.gameObject.AddComponent<ShowDirector>();
            Director.Stage = Stage;
            Director.Portal = portal;
            Director.Hands = Hands; Director.Head = Head;
            Director.Site = site;
            Director.Magician = MagicianActor.Create(portal.Lift);
            Director.Coin = CoinProp.Create(Stage);
            Director.Case = VelvetCase.Create(Stage);
            Director.Token = EncoreToken.Create(Stage);
            Director.Bird = BirdProp.Create(Stage);
            Director.Captions = CaptionPresenter.Create(Stage, Head);
            Director.Audio = AudioDirector.Create(Stage);
            Director.Audio.Muted = Options.Mute;
            Director.Curtains = WallCurtainStager.Create(Stage, Room);
            Director.Title = TitleCard.Create(Stage);
            Director.OneHanded = NightStore.OneHanded;
            Director.Autopilot = Options.Autopilot;
            Director.NightCompleted = NightStore.CompleteNight;
            Director.DebugGesture = OnDebugGesture;
            Director.HouseLights = level => HouseLights?.Invoke(level);

            int night = Options.Night > 0 ? Options.Night : NightStore.CurrentNight;
            Director.Init(night);
            if (!string.IsNullOrEmpty(Options.Beat) && Enum.TryParse(Options.Beat, true, out ShowState beat)) Director.JumpTo(beat);
            else Director.Begin();
            Ready = true;
            Debug.Log("[PARLOR] ready in " + (Time.realtimeSinceStartup - t0).ToString("F2") + " s (since launch "
                      + Time.realtimeSinceStartup.ToString("F2") + " s), tier " + site.Tier + ", night " + night);
        }

        // Hidden: a 3-second pinch on the Encore token. First time: one-handed mode toggles. Twice in a row: full reset.
        float _lastDebug = -100f;
        void OnDebugGesture()
        {
            if (Time.unscaledTime - _lastDebug < 12f) { NightStore.ResetAll(); Debug.Log("[PARLOR] progress reset"); }
            else { NightStore.OneHanded = !NightStore.OneHanded; Director.OneHanded = NightStore.OneHanded; Debug.Log("[PARLOR] one-handed " + NightStore.OneHanded); }
            _lastDebug = Time.unscaledTime;
        }

        /// <summary>
        /// Tears the stage down and finds it a place again from the user's current pose (recenter, the user moved
        /// to another seat, or a test moved the simulated head). The night and progress are untouched.
        /// </summary>
        public void Replace()
        {
            StopAllCoroutines();
            Ready = false;
            Time.timeScale = 1f;
            if (Stage) Destroy(Stage.gameObject);
            Stage = null; Director = null;
            StartCoroutine(Start());
        }

        /// <summary>Headset taken off, app paused, or focus lost.</summary>
        public void SetPaused(bool paused) { if (Director) Director.SetPaused(paused); }

#if !UNITY_EDITOR
        // In the Editor, focus moves to other windows all the time; only a real device pauses the show.
        void OnApplicationPause(bool paused) { SetPaused(paused); }
#endif

        void OnDestroy()
        {
            HouseLights?.Invoke(1f);
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
            Mats.ClearCache();
        }
    }
}
