#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Editor only. Works out where a person would sit at a table in the loaded room, so simulator runs can put the
    /// simulated head there (through Meta XR Operator) before the stage is placed. Never in a build.
    /// </summary>
    public static class DebugSeat
    {
        /// <summary>
        /// "x y z qx qy qz qw | note": head pose in OpenXR conventions (right-handed, -Z forward) for a seat at the
        /// index-th best table. Empty string when the room has no usable table.
        /// </summary>
        public static string OpenXrSeat(int index = 0, float pitchDown = 32f)
        {
            var app = ParlorApp.Instance;
            if (!app || app.Room == null || !app.Room.Available) return "";
            var tables = new List<TableSurface>();
            app.Room.Tables(tables);
            Vector3 from = app.Head.Head.position;
            // table-height surfaces a seated person can use, biggest first
            tables.RemoveAll(t => t.Pose.position.y < 0.30f || t.Pose.position.y > 0.90f || Mathf.Min(t.Size.x, t.Size.y) < 0.40f);
            tables.Sort((a, b) => (b.Size.x * b.Size.y).CompareTo(a.Size.x * a.Size.y));
            if (tables.Count == 0) return "";
            var table = tables[Mathf.Clamp(index, 0, tables.Count - 1)];

            // sit at the middle of the long edge nearest to where the head is now
            Vector3 r = table.Pose.rotation * Vector3.right, f = table.Pose.rotation * Vector3.forward;
            bool longIsX = table.Size.x >= table.Size.y;
            Vector3 across = longIsX ? f : r;
            float half = (longIsX ? table.Size.y : table.Size.x) * 0.5f;
            if (Vector3.Dot(from - table.Pose.position, across) < 0f) across = -across;
            Vector3 eye = table.Pose.position + across * (half + 0.24f) + Vector3.up * 0.44f;
            Quaternion look = Quaternion.LookRotation(-across, Vector3.up) * Quaternion.Euler(pitchDown, 0, 0);

            // Unity (left-handed, +Z forward) → OpenXR (right-handed, -Z forward): negate z; quaternion (-x, -y, z, w)
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "{0:F3} {1:F3} {2:F3} {3:F4} {4:F4} {5:F4} {6:F4} | table {7} m at height {8:F2} m",
                eye.x, eye.y, -eye.z, -look.x, -look.y, look.z, look.w, table.Size.ToString("F2"), table.Pose.position.y);
        }

        /// <summary>"x y z" in OpenXR conventions for a named point of the running show (simulated-hand targets).</summary>
        public static string OpenXrPoint(string name)
        {
            var app = ParlorApp.Instance;
            if (!app || !app.Director) return "";
            var d = app.Director; Transform st = app.Stage;
            Vector3 p;
            switch (name)
            {
                case "coin": p = d.Coin.transform.position; break;
                case "aboveCoin": p = d.Coin.transform.position + st.up * 0.10f; break;
                case "place": p = st.TransformPoint(new Vector3(0.02f, 0.012f, -0.15f)); break;
                case "abovePlace": p = st.TransformPoint(new Vector3(0.02f, 0.10f, -0.15f)); break;
                case "palm": p = st.TransformPoint(new Vector3(0.0f, 0.10f, -0.22f)); break;
                case "case": p = d.Case.CoverPoint + st.up * 0.05f; break;
                case "aboveCase": p = d.Case.CoverPoint + st.up * 0.26f; break;
                case "token": p = d.Token.transform.position; break;
                case "beforeToken": p = d.Token.transform.position - st.forward * 0.08f; break;
                case "card0": case "card1": case "card2":
                    p = st.TransformPoint(Layout.CardRowCentre + Vector3.right * ((name[4] - '1') * Layout.CardSpacing)); break;
                case "restL": p = st.TransformPoint(new Vector3(-0.20f, 0.10f, -0.36f)); break;
                default: p = st.TransformPoint(new Vector3(0.20f, 0.10f, -0.36f)); break;   // "rest"
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3}", p.x, p.y, -p.z);
        }

        /// <summary>What the show currently sees of each hand, for calibrating simulated poses.</summary>
        public static string Hands()
        {
            var app = ParlorApp.Instance;
            if (!app) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Handed h in new[] { Handed.Left, Handed.Right })
            {
                var s = app.Hands.Get(h);
                sb.Append(h + ": tracked=" + s.Tracked);
                if (s.Tracked)
                    sb.Append(" palm=" + s.PalmPosition.ToString("F2") + " normal.up=" + Vector3.Dot(s.PalmNormal, Vector3.up).ToString("F2")
                              + " curl=" + s.Curl.ToString("F2") + " pinch=" + s.Pinch.ToString("F2")
                              + " palmUpOpen=" + PoseMath.PalmUpOpen(s) + " fist=" + PoseMath.Fist(s) + " flatDown=" + PoseMath.PalmDownFlat(s));
                sb.Append(" | ");
            }
            if (app.Director) sb.Append("gate=" + app.Director.ActiveGateName + " beat=" + app.Director.Model.State);
            return sb.ToString();
        }
    }
}
#endif
