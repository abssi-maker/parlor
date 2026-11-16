using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Parlor.Tests
{
    /// <summary>Gate G3: the show graph cannot softlock. Every state is reachable, every state exits, every gate times out.</summary>
    public class ShowGraphTests
    {
        [Test]
        public void EveryStateIsReachableFromTheFirst()
        {
            var seen = new HashSet<ShowState>();
            var s = ShowGraph.First;
            for (int i = 0; i < 32 && seen.Add(s); i++) s = ShowGraph.Get(s).Next;
            foreach (ShowState state in System.Enum.GetValues(typeof(ShowState))) Assert.Contains(state, new List<ShowState>(seen));
        }

        [Test]
        public void EveryStateBeforeEncoreExitsOnItsOwn()
        {
            foreach (var spec in ShowGraph.All)
            {
                Assert.AreNotEqual(spec.State, spec.Next, spec.State + " loops to itself");
                Assert.Less(spec.HintAt, spec.AltAt); Assert.Less(spec.AltAt, spec.AutoAt);
                if (spec.State != ShowState.Encore) Assert.LessOrEqual(spec.AutoAt, 30f, spec.State + " can wait forever");
            }
        }

        [Test]
        public void ModelRunsAFullShowAndTheEncoreRestartsAtArrival()
        {
            var m = new ShowModel(1);
            int steps = 0;
            while (m.State != ShowState.Encore && steps++ < 20) m.Advance();
            Assert.AreEqual(ShowState.Encore, m.State);
            Assert.AreEqual(1, m.Runs);
            Assert.AreEqual(ShowState.Arrival, m.Advance());
        }

        [Test]
        public void CuttingThePalmTrickSkipsItWithoutBreakingTheGraph()
        {
            var m = new ShowModel(1) { PalmCoinCut = true };
            var visited = new List<ShowState>();
            while (m.State != ShowState.Encore) { visited.Add(m.State); m.Advance(); }
            CollectionAssert.DoesNotContain(visited, ShowState.PalmCoin);
            CollectionAssert.Contains(visited, ShowState.Lesson);
        }
    }

    public class BeatGateTests
    {
        [Test]
        public void AnIgnoredGateHintsOffersThePinchAndThenTimesOut()
        {
            var g = new BeatGate(6f, 15f, 30f);
            var events = new List<GateEvent>();
            for (int i = 0; i < 400 && !g.Done; i++) { var e = g.Tick(0.1f, false, false, false); if (e != GateEvent.None) events.Add(e); }
            CollectionAssert.AreEqual(new[] { GateEvent.Hint, GateEvent.AltOffered, GateEvent.Timeout }, events);
            Assert.LessOrEqual(g.Elapsed, 30.2f);
        }

        [Test]
        public void ThePoseMustBeHeldAndLooseTrackingDoesNotResetIt()
        {
            var g = new BeatGate(6f, 15f, 30f, 0.4f);
            Assert.AreEqual(GateEvent.None, g.Tick(0.1f, true, true, false));
            Assert.AreEqual(GateEvent.None, g.Tick(0.1f, false, true, false));   // strict pose flickers, loose pose holds
            Assert.AreEqual(GateEvent.None, g.Tick(0.1f, false, true, false));
            Assert.AreEqual(GateEvent.Success, g.Tick(0.1f, true, true, false));
        }

        [Test]
        public void ThePinchAlternativeNeedsAFreshPinch()
        {
            var g = new BeatGate(1f, 2f, 30f);
            for (int i = 0; i < 25; i++) g.Tick(0.1f, false, false, true);        // pinch already held when the alternative opens
            Assert.IsTrue(g.AltOpen); Assert.IsFalse(g.Done);
            g.Tick(0.1f, false, false, false);
            Assert.AreEqual(GateEvent.AltSuccess, g.Tick(0.1f, false, false, true));
        }

        [Test]
        public void TheClockStopsWhileHandsAreLost()
        {
            var g = new BeatGate(6f, 15f, 30f);
            for (int i = 0; i < 1000; i++) g.Tick(0.1f, false, false, false, inputAvailable: false);
            Assert.IsFalse(g.Done); Assert.AreEqual(0f, g.Elapsed);
        }
    }

    /// <summary>Gate G17: scripted head poses pick the intended card; ambiguous dwell asks for a poke.</summary>
    public class GazeDwellTests
    {
        static readonly Vector3 Eye = new Vector3(0, 0.40f, -0.57f);
        static Vector3[] Cards()
        {
            var c = new Vector3[3];
            for (int i = 0; i < 3; i++) c[i] = Layout.CardRowCentre + Vector3.right * (i - 1) * Layout.CardSpacing;
            return c;
        }

        static Pose LookAt(Vector3 target, float yawNoise, float pitchNoise)
        {
            var q = Quaternion.LookRotation(target - Eye, Vector3.up) * Quaternion.Euler(pitchNoise, yawNoise, 0);
            return new Pose(Eye, q);
        }

        [Test]
        public void HeadGazePicksTheIntendedCardInAtLeastNineOfTenRuns()
        {
            var rnd = new System.Random(7);
            var cards = Cards();
            int right = 0, runs = 0;
            for (int target = 0; target < 3; target++)
                for (int run = 0; run < 10; run++, runs++)
                {
                    var p = new GazeDwellPicker(3);
                    for (float t = 0; t < 4f; t += 1f / 72f)
                    {
                        // people glance at the others for the first second, then settle; heads under-rotate and jitter
                        int look = t < 1f ? (int)(t * 3f) % 3 : target;
                        Vector3 aim = Vector3.Lerp((cards[0] + cards[2]) * 0.5f, cards[look], 0.8f);
                        float n1 = (float)(rnd.NextDouble() - 0.5) * 3f, n2 = (float)(rnd.NextDouble() - 0.5) * 3f;
                        p.Accumulate(LookAt(aim, n1, n2), cards, Vector3.up, 1f / 72f);
                    }
                    if (p.Pick(out bool ambiguous) == target && !ambiguous) right++;
                }
            Assert.GreaterOrEqual(right, runs * 9 / 10, right + " of " + runs);
        }

        [Test]
        public void EvenlySplitDwellIsAmbiguous()
        {
            var cards = Cards();
            var p = new GazeDwellPicker(3);
            for (int i = 0; i < 288; i++) p.Accumulate(LookAt(cards[i % 2 == 0 ? 0 : 2], 0, 0), cards, Vector3.up, 1f / 72f);
            p.Pick(out bool ambiguous);
            Assert.IsTrue(ambiguous);
        }

        [Test]
        public void LookingAwayFromTheRowIsAmbiguous()
        {
            var cards = Cards();
            var p = new GazeDwellPicker(3);
            for (int i = 0; i < 288; i++) p.Accumulate(new Pose(Eye, Quaternion.LookRotation(Vector3.up + Vector3.forward * 0.2f)), cards, Vector3.up, 1f / 72f);
            p.Pick(out bool ambiguous);
            Assert.IsTrue(ambiguous);
        }
    }

    /// <summary>Gate G4 (maths half): the stage lands on any reachable table, inside it, facing away from the user.</summary>
    public class StagePlacerTests
    {
        static TableSurface Table(Vector3 centre, float yaw, float w, float d) =>
            new TableSurface { Pose = new Pose(centre, Quaternion.Euler(0, yaw, 0)), Size = new Vector2(w, d), Uuid = "t" };

        static Pose Seated(Vector3 pos, float yaw, float pitchDown = 20f) => new Pose(pos, Quaternion.Euler(pitchDown, yaw, 0));

        [Test]
        public void StageSitsOnTheTableInReachAndFacesAwayFromTheUser([Values(0f, 17f, 45f, 90f, 133f, 270f)] float yaw,
                                                                    [Values(0.5f, 0.8f, 1.6f, 2.4f)] float width)
        {
            var t = Table(new Vector3(1, 0.74f, 2), yaw, width, 0.7f);
            // the user sits at the table's near long edge, a little off-centre, facing it
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            Vector3 seat = t.Pose.position + r * new Vector3(width * 0.2f, 0.42f, -0.35f - 0.22f);
            var head = Seated(seat, yaw);
            Assert.IsTrue(StagePlacer.TryTable(t, head, out Pose stage, out Rect rect, out _));

            Vector3 local = Quaternion.Inverse(t.Pose.rotation) * (stage.position - t.Pose.position);
            Assert.LessOrEqual(Mathf.Abs(local.x), width * 0.5f); Assert.LessOrEqual(Mathf.Abs(local.z), 0.35f);
            Assert.AreEqual(0f, local.y, 1e-4f);
            Vector3 toStage = Vector3.ProjectOnPlane(stage.position - head.position, Vector3.up);
            Assert.LessOrEqual(toStage.magnitude, StagePlacer.MaxReach);
            Assert.Greater(Vector3.Dot(stage.forward, toStage.normalized), 0.7f, "stage must face away from the user");
            Assert.IsTrue(rect.Contains(Vector2.zero));
            // every corner of the reported rectangle is really on the table
            foreach (var c in new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) })
            {
                Vector3 w = stage.position + stage.rotation * new Vector3(c.x, 0, c.y);
                Vector3 l = Quaternion.Inverse(t.Pose.rotation) * (w - t.Pose.position);
                Assert.LessOrEqual(Mathf.Abs(l.x), width * 0.5f + 0.002f); Assert.LessOrEqual(Mathf.Abs(l.z), 0.352f);
            }
        }

        [Test]
        public void WholeFootprintRestsOnTheTable([Values(0f, 45f, 133f)] float yaw, [Values(0.5f, 1.0f, 2.2f)] float width,
                                                 [Values(-0.6f, 0f, 0.6f)] float sideways)
        {
            var t = Table(new Vector3(1, 0.85f, 2), yaw, width, 0.8f);
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            // sitting at the near edge, possibly level with a corner: the stage must still not hang off
            Vector3 seat = t.Pose.position + r * new Vector3(sideways * width * 0.5f, 0.44f, -0.4f - 0.24f);
            if (!StagePlacer.TryTable(t, Seated(seat, yaw, 32f), out Pose stage, out _, out _)) return;   // out of reach is allowed
            Assert.IsTrue(StagePlacer.FootprintFits(t, stage));
            Rect f = StagePlacer.Footprint;
            foreach (var c in new[] { new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMax, f.yMax), new Vector2(f.xMin, f.yMax) })
            {
                Vector3 l = Quaternion.Inverse(t.Pose.rotation) * (stage.position + stage.rotation * new Vector3(c.x, 0, c.y) - t.Pose.position);
                Assert.LessOrEqual(Mathf.Abs(l.x), width * 0.5f); Assert.LessOrEqual(Mathf.Abs(l.z), 0.4f);
            }
        }

        [Test]
        public void StageStepsAroundAnObjectOnTheTableAndSkipsTablesTooSmall()
        {
            var t = Table(new Vector3(0, 0.85f, 0.7f), 0, 2.0f, 0.9f);
            var head = Seated(new Vector3(0, 1.29f, 0), 0, 32f);
            Assert.IsTrue(StagePlacer.TryTable(t, head, out Pose free, out _, out _));
            // a 30 cm board-game box exactly where the stage would go
            var box = new GameObject("box").transform;
            box.position = free.position + Vector3.up * 0.04f;
            var obstacles = new List<RoomBox> { new RoomBox { WorldToLocal = box.worldToLocalMatrix, Local = new Bounds(Vector3.zero, new Vector3(0.30f, 0.08f, 0.30f)),
                                                              TopY = 0.93f, BottomY = 0.85f } };
            Assert.IsTrue(StagePlacer.TryTable(t, head, out Pose moved, out _, out _, obstacles));
            Assert.IsFalse(StagePlacer.Blocked(t, moved, obstacles));
            Assert.Greater(Vector3.Distance(free.position, moved.position), 0.2f);
            Object.DestroyImmediate(box.gameObject);
            // a 40 x 30 cm side table cannot carry the trapdoor, coin and case
            Assert.IsFalse(StagePlacer.TryTable(Table(new Vector3(0, 0.6f, 0.55f), 0, 0.40f, 0.30f), head, out _, out _, out _));
        }

        [Test]
        public void TablesOutOfReachOrAboveTheChinAreRejected()
        {
            var head = Seated(new Vector3(0, 1.15f, 0), 0);
            Assert.IsFalse(StagePlacer.TryTable(Table(new Vector3(0, 0.74f, 2.5f), 0, 1.2f, 0.7f), head, out _, out _, out _), "far table");
            Assert.IsFalse(StagePlacer.TryTable(Table(new Vector3(0, 1.20f, 0.5f), 0, 1.2f, 0.7f), head, out _, out _, out _), "shelf above the eyes");
            Assert.IsFalse(StagePlacer.TryTable(Table(new Vector3(0, 0.74f, 0.5f), 0, 0.15f, 0.15f), head, out _, out _, out _), "tiny surface");
        }

        [Test]
        public void FloatingFallbackIsInFrontOfAndBelowTheEyes()
        {
            var head = Seated(new Vector3(3, 1.2f, -2), 75f, 35f);
            Pose p = StagePlacer.Floating(head);
            Assert.AreEqual(head.position.y - Layout.ExpectedEye.y, p.position.y, 1e-4f);
            Vector3 flat = Vector3.ProjectOnPlane(p.position - head.position, Vector3.up);
            Assert.AreEqual(-Layout.ExpectedEye.z, flat.magnitude, 1e-3f);
            Assert.Greater(Vector3.Dot(flat.normalized, Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized), 0.99f);
            Assert.AreEqual(1f, Vector3.Dot(p.up, Vector3.up), 1e-4f);
        }
    }
}
