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
        const float EdgeMargin = 0.16f;

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

                // Returning user: same table as last time → the stage goes back on exactly the same spot.
                if (NightStore.TryGetTableOffset(out string savedUuid, out Vector3 savedPos, out float savedYaw))
                    foreach (var t in tables)
                        if (t.Uuid == savedUuid)
                        {
                            Pose p = new Pose(t.Pose.position + t.Pose.rotation * savedPos, t.Pose.rotation * Quaternion.Euler(0, savedYaw, 0));
                            Apply(stage, site, p, RectOnTable(t, p), 1, t.Uuid, true);
                            yield break;
                        }

                float best = float.MaxValue; Pose bestPose = default; Rect bestRect = default; TableSurface bestTable = default;
                foreach (var t in tables)
                    if (TryTable(t, h, out Pose p, out Rect r, out float score) && score < best)
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
        /// Stage pose on one table, or false if the table is out of reach, too high, too low or tilted.
        /// The stage goes to a reachable point in front of the user (not the middle of a two-metre desk) and its
        /// axes snap to the table's edges when the user sits roughly square to it, so the trapdoor lines up with the wood.
        /// </summary>
        public static bool TryTable(TableSurface t, Pose head, out Pose stagePose, out Rect tableRect, out float score)
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

            Vector2 m = new Vector2(Mathf.Min(EdgeMargin, half.x * 0.5f), Mathf.Min(EdgeMargin, half.y * 0.5f));
            Vector2 want = eye + fwd * Ahead;
            Vector2 c = new Vector2(Mathf.Clamp(want.x, -half.x + m.x, half.x - m.x), Mathf.Clamp(want.y, -half.y + m.y, half.y - m.y));
            float reach = (c - eye).magnitude;
            if (reach > MaxReach) return false;

            Vector2 d = c - eye;
            d = d.sqrMagnitude > 0.0025f ? d.normalized : fwd;
            // snap to the nearest table axis when within 35°
            Vector2 axis = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2(Mathf.Sign(d.x), 0) : new Vector2(0, Mathf.Sign(d.y));
            if (Vector2.Angle(d, axis) < 35f) d = axis;

            Quaternion rot = t.Pose.rotation * Quaternion.LookRotation(new Vector3(d.x, 0, d.y), Vector3.up);
            stagePose = new Pose(t.Pose.position + t.Pose.rotation * new Vector3(c.x, 0, c.y), rot);
            tableRect = RectOnTable(t, stagePose);
            // Prefer the table the user is facing and sitting at: little clamping, short reach.
            score = (want - c).magnitude + gap + Mathf.Abs(reach - Ahead) * 0.5f;
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
