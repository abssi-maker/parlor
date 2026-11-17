using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Finds where the stage goes. Tiers fall through automatically and never dead-end:
    ///   1. a real table from scene data, within the seated user's reach;
    ///   2. (pinch-to-place on any horizontal surface: added on top of this class)
    ///   3. a small virtual parlour table floating in front of the user.
    /// The maths is static and pure so PlayMode tests can run it against any room.
    /// </summary>
    public static class StagePlacer
    {
        /// <summary>How far in front of the eyes (horizontally) the stage origin should sit.</summary>
        public const float Ahead = 0.52f;
        public const float MaxReach = 0.80f;
        /// <summary>
        /// What must physically rest on the table, in stage XZ (x = right, y = away from the user): the trapdoor and its
        /// rim, the coin's landing spot and the velvet case in front of it. Every corner must be on the tabletop, so
        /// nothing ever hangs over an edge or floats above a chair.
        /// </summary>
        public static readonly Rect Footprint = Rect.MinMaxRect(-0.215f, -0.255f, 0.215f, 0.19f);
        const float FootMargin = 0.015f;
        const float GridStep = 0.04f;

        public static IEnumerator Place(Transform stage, StageSite site, IRoomSource room, IHeadSource head, int forcedTier = 0, Pose? savedAnchor = null)
        {
            // Wait briefly for a real head pose and for the room. Never longer: the magician must be on stage in < 10 s.
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.5f && head.Head.position.sqrMagnitude < 0.01f) yield return null;
            while (room != null && !room.Settled && Time.realtimeSinceStartup - t0 < 4f) yield return null;

            Pose h = head.Head;
            if (forcedTier != 3 && room != null && room.Available)
            {
                var tables = new List<TableSurface>();
                room.Tables(tables);
                var obstacles = new List<RoomBox>();
                room.Obstacles(obstacles);
                var clutter = new List<Vector3>();
                room.Clutter(clutter);

                // Returning user: same table as last time → the stage goes back on exactly the same spot, but only if
                // that spot is still reachable from where they sit and still clear (things move on real tables).
                if (NightStore.TryGetTableOffset(out string savedUuid, out Vector3 savedPos, out float savedYaw))
                    foreach (var t in tables)
                        if (t.Uuid == savedUuid)
                        {
                            Pose p = new Pose(t.Pose.position + t.Pose.rotation * savedPos, t.Pose.rotation * Quaternion.Euler(0, savedYaw, 0));
                            float reach = Vector3.ProjectOnPlane(p.position - h.position, Vector3.up).magnitude;
                            if (reach <= MaxReach && FootprintFits(t, p) && !Blocked(t, p, obstacles) && !Cluttered(p, clutter))
                            {
                                Apply(stage, site, p, RectOnTable(t, p), 1, t.Uuid, true);
                                yield break;
                            }
                        }

                float best = float.MaxValue; Pose bestPose = default; Rect bestRect = default; TableSurface bestTable = default;
                foreach (var t in tables)
                    if (TryTable(t, h, out Pose p, out Rect r, out float score, obstacles, clutter) && score < best)
                    { best = score; bestPose = p; bestRect = r; bestTable = t; }
                if (best < float.MaxValue)
                {
                    Apply(stage, site, bestPose, bestRect, 1, bestTable.Uuid, false);
                    Quaternion inv = Quaternion.Inverse(bestTable.Pose.rotation);
                    NightStore.SaveTableOffset(bestTable.Uuid, inv * (bestPose.position - bestTable.Pose.position),
                                               (inv * bestPose.rotation).eulerAngles.y);
                    yield break;
                }
            }
            // No table to stand on. If last session's stage anchor is still within reach and level, the floating table
            // goes back exactly where it was; otherwise it appears in front of the user.
            if (forcedTier == 0 && savedAnchor.HasValue)
            {
                Pose a = savedAnchor.Value;
                Vector3 flat = Vector3.ProjectOnPlane(a.position - h.position, Vector3.up);
                float below = h.position.y - a.position.y;
                if (flat.magnitude < MaxReach + 0.2f && below > 0.12f && below < 1.1f && Vector3.Dot(a.rotation * Vector3.up, Vector3.up) > 0.94f)
                {
                    Apply(stage, site, a, new StageSite().TableRect, 3, "", true);
                    yield break;
                }
            }
            Apply(stage, site, Floating(h), new StageSite().TableRect, 3, "", false);
        }

        static void Apply(Transform stage, StageSite site, Pose pose, Rect rect, int tier, string uuid, bool restored)
        {
            stage.SetPositionAndRotation(pose.position, pose.rotation);
            site.Tier = tier; site.TableRect = rect; site.TableUuid = uuid ?? ""; site.Restored = restored;
        }

        /// <summary>True when every corner of the footprint (plus a small margin) lies on the tabletop.</summary>
        public static bool FootprintFits(TableSurface t, Pose stagePose)
        {
            Quaternion inv = Quaternion.Inverse(t.Pose.rotation);
            Vector2 half = t.Size * 0.5f - Vector2.one * FootMargin;
            if (half.x <= 0f || half.y <= 0f) return false;
            Rect f = Footprint;
            foreach (var c in new[] { new Vector2(f.xMin, f.yMin), new Vector2(f.xMax, f.yMin), new Vector2(f.xMax, f.yMax), new Vector2(f.xMin, f.yMax) })
            {
                Vector3 l = inv * (stagePose.position + stagePose.rotation * new Vector3(c.x, 0, c.y) - t.Pose.position);
                if (Mathf.Abs(l.x) > half.x + 1e-4f || Mathf.Abs(l.z) > half.y + 1e-4f) return false;
            }
            return true;
        }

        /// <summary>True when a scanned object standing on this table overlaps the footprint.</summary>
        public static bool Blocked(TableSurface t, Pose stagePose, List<RoomBox> obstacles)
        {
            if (obstacles == null || obstacles.Count == 0) return false;
            float top = t.Pose.position.y;
            Rect f = Footprint;
            foreach (var o in obstacles)
            {
                // only things that rise above the tabletop and start below a hand's height over it
                if (o.TopY < top + 0.015f || o.BottomY > top + 0.45f) continue;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        Vector3 w = stagePose.position + stagePose.rotation *
                                    new Vector3(Mathf.Lerp(f.xMin, f.xMax, i / 4f), 0, Mathf.Lerp(f.yMin, f.yMax, j / 4f));
                        if (o.ContainsXZ(w, 0.02f)) return true;
                    }
            }
            return false;
        }

        /// <summary>
        /// True when anything real stands inside the footprint (plus a 3 cm margin): scene-mesh points on the tabletop.
        /// </summary>
        /// <summary>How many clutter samples fall inside the footprint (2 cm samples: a rough area of overlap).</summary>
        public static int ClutterCount(Pose stagePose, List<Vector3> clutter)
        {
            if (clutter == null) return 0;
            Quaternion inv = Quaternion.Inverse(stagePose.rotation);
            Rect f = Footprint; int n = 0;
            foreach (var w in clutter)
            {
                Vector3 l = inv * (w - stagePose.position);
                if (l.x > f.xMin && l.x < f.xMax && l.z > f.yMin && l.z < f.yMax) n++;
            }
            return n;
        }

        public static bool Cluttered(Pose stagePose, List<Vector3> clutter, float margin = 0.03f)
        {
            if (clutter == null || clutter.Count == 0) return false;
            Quaternion inv = Quaternion.Inverse(stagePose.rotation);
            Rect f = Footprint;
            foreach (var w in clutter)
            {
                Vector3 l = inv * (w - stagePose.position);
                if (l.x > f.xMin - margin && l.x < f.xMax + margin && l.z > f.yMin - margin && l.z < f.yMax + margin) return true;
            }
            return false;
        }

        /// <summary>Tier 3: the stage 40 cm below eye level, about half a metre ahead, facing away from the user.</summary>
        public static Pose Floating(Pose head)
        {
            Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            Vector3 pos = head.position + f * -Layout.ExpectedEye.z;
            pos.y = head.position.y - Layout.ExpectedEye.y;
            return new Pose(pos, Quaternion.LookRotation(f, Vector3.up));
        }

        /// <summary>
        /// Stage pose on one table, or false if the table is out of reach, too high, too low, tilted, too small for the
        /// footprint, or covered where the user could reach. The stage goes to a reachable point in front of the user
        /// (not the middle of a two-metre desk) and its axes snap to the table's edges when the user sits roughly square to it.
        /// </summary>
        public static bool TryTable(TableSurface t, Pose head, out Pose stagePose, out Rect tableRect, out float score,
                                    List<RoomBox> obstacles = null, List<Vector3> clutter = null)
        {
            stagePose = default; tableRect = default; score = float.MaxValue;
            Vector3 up = t.Pose.rotation * Vector3.up;
            if (Vector3.Dot(up, Vector3.up) < 0.94f) return false;
            Quaternion inv = Quaternion.Inverse(t.Pose.rotation);
            Vector3 l = inv * (head.position - t.Pose.position);
            if (l.y < 0.12f || l.y > 1.10f) return false;                    // table above the chin, or far below a seated user
            Vector2 half = t.Size * 0.5f;
            if (half.x < 0.10f || half.y < 0.10f) return false;              // a shelf edge, not a table

            Vector2 eye = new Vector2(l.x, l.z);
            Vector2 nearest = new Vector2(Mathf.Clamp(eye.x, -half.x, half.x), Mathf.Clamp(eye.y, -half.y, half.y));
            float gap = (nearest - eye).magnitude;
            if (gap > 0.70f) return false;

            Vector3 f3 = inv * head.forward;
            Vector2 fwd = new Vector2(f3.x, f3.z);
            if (fwd.sqrMagnitude < 0.04f) fwd = -eye;                         // looking straight down: face the table centre
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector2.up;
            fwd.Normalize();
            Vector2 want = eye + fwd * Ahead;

            // Search the tabletop on a 4 cm grid for the spot nearest to "half a metre in front of the eyes" where the
            // whole footprint rests on the table, within reach, clear of scanned objects. If real clutter covers every
            // reachable spot, a second pass accepts the spot that overlaps the least of it: the show always stands on
            // the user's real table rather than on a virtual one.
            bool found = false; float best = float.MaxValue; Pose bestPose = default;
            int nx = Mathf.Max(1, Mathf.CeilToInt(t.Size.x / GridStep)), nz = Mathf.Max(1, Mathf.CeilToInt(t.Size.y / GridStep));
            for (int pass = 0; pass < 2 && !found; pass++)
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    Vector2 c = new Vector2(-half.x + t.Size.x * i / nx, -half.y + t.Size.y * j / nz);
                    float reach = (c - eye).magnitude;
                    if (reach > MaxReach) continue;
                    float s = (want - c).magnitude + Mathf.Abs(reach - Ahead) * 0.5f;
                    if (s >= best) continue;
                    // (pass 1 adds the overlap with clutter below, once the pose is known)

                    Vector2 d = c - eye;
                    d = d.sqrMagnitude > 0.0025f ? d.normalized : fwd;
                    // snap to the nearest table axis when within 35°, so the trapdoor lines up with the wood
                    Vector2 axis = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y));
                    if (Vector2.Angle(d, axis) < 35f) d = axis;
                    var p = new Pose(t.Pose.position + t.Pose.rotation * new Vector3(c.x, 0, c.y),
                                     t.Pose.rotation * Quaternion.LookRotation(new Vector3(d.x, 0, d.y), Vector3.up));
                    if (!FootprintFits(t, p) || Blocked(t, p, obstacles)) continue;
                    if (pass == 0) { if (Cluttered(p, clutter)) continue; }
                    else { s += 0.002f * ClutterCount(p, clutter); if (s >= best) continue; }
                    best = s; bestPose = p; found = true;
                }
            if (!found) return false;

            stagePose = bestPose;
            tableRect = RectOnTable(t, stagePose);
            // Prefer the table the user is facing and sitting at: little displacement, short reach.
            score = best + gap;
            return true;
        }

        /// <summary>
        /// The largest rectangle aligned with the stage's axes, containing the stage origin, that fits on the table.
        /// In stage XZ: x = right, y = away from the user.
        /// </summary>
        public static Rect RectOnTable(TableSurface t, Pose stagePose)
        {
            Quaternion inv = Quaternion.Inverse(t.Pose.rotation);
            Vector3 o3 = inv * (stagePose.position - t.Pose.position);
            Vector2 o = new Vector2(o3.x, o3.z), half = t.Size * 0.5f;
            Vector3 r3 = inv * (stagePose.rotation * Vector3.right), f3 = inv * (stagePose.rotation * Vector3.forward);
            Vector2 r = new Vector2(r3.x, r3.z).normalized, f = new Vector2(f3.x, f3.z).normalized;

            float Ray(Vector2 dir)
            {
                float best = 10f;
                if (Mathf.Abs(dir.x) > 1e-4f) best = Mathf.Min(best, ((dir.x > 0 ? half.x : -half.x) - o.x) / dir.x);
                if (Mathf.Abs(dir.y) > 1e-4f) best = Mathf.Min(best, ((dir.y > 0 ? half.y : -half.y) - o.y) / dir.y);
                return Mathf.Max(0f, best);
            }
            bool Inside(Vector2 p) => Mathf.Abs(p.x) <= half.x + 1e-3f && Mathf.Abs(p.y) <= half.y + 1e-3f;

            float xr = Ray(r), xl = Ray(-r), zf = Ray(f), zn = Ray(-f);
            for (int i = 0; i < 24; i++)
            {
                if (Inside(o + r * xr + f * zf) && Inside(o - r * xl + f * zf) && Inside(o + r * xr - f * zn) && Inside(o - r * xl - f * zn)) break;
                xr *= 0.9f; xl *= 0.9f; zf *= 0.9f; zn *= 0.9f;
            }
            return Rect.MinMaxRect(-xl, -zn, xr, zf);
        }
    }
}
