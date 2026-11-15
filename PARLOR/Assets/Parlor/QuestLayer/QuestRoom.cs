using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace Parlor.Quest
{
    /// <summary>
    /// The real room, from MR Utility Kit scene data: TABLE anchors to stand the stage on and WALL_FACE anchors for the
    /// curtain call. If MRUK is missing, the user denied the scene permission, or no room was ever captured, the room
    /// simply reports "not available" and the show uses its floating table. Nothing here can block or throw the show.
    ///
    /// Geometry is read defensively: for a volume (a table) the top is whichever local face points up, so the code
    /// does not depend on the anchor's axis convention.
    /// </summary>
    public sealed class QuestRoom : MonoBehaviour, IRoomSource
    {
        public float GiveUpAfter = 4f;
        float _t0;
        bool _loaded;

        void Awake()
        {
            _t0 = Time.realtimeSinceStartup;
            if (MRUK.Instance) MRUK.Instance.RegisterSceneLoadedCallback(() => { _loaded = true; Debug.Log("[PARLOR] room loaded: " + Describe()); });
        }

        public bool Settled => _loaded || !MRUK.Instance || Time.realtimeSinceStartup - _t0 > GiveUpAfter;
        public bool Available => _loaded && MRUK.Instance && MRUK.Instance.GetCurrentRoom() != null;

        string Describe()
        {
            var t = new List<TableSurface>(); var w = new List<WallSurface>();
            Tables(t); Walls(w);
            return t.Count + " table surface(s), " + w.Count + " wall(s)";
        }

        public void Tables(List<TableSurface> into)
        {
            if (!Available) return;
            // Tables first, but any labelled furniture with a flat top will do (a desk captured as OTHER, a low
            // storage unit, a couch arm is too narrow and gets rejected by StagePlacer).
            const MRUKAnchor.SceneLabels flat = MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.OTHER | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED;
            foreach (var room in MRUK.Instance.Rooms)
                foreach (var a in room.Anchors)
                    if (a && a.HasAnyLabel(flat) && TryTop(a, out TableSurface s)) into.Add(s);
        }

        static bool TryTop(MRUKAnchor a, out TableSurface s)
        {
            s = default;
            Transform t = a.transform;
            Vector3[] axes = { t.right, t.up, t.forward };
            if (a.VolumeBounds.HasValue)
            {
                Bounds b = a.VolumeBounds.Value;
                int upAxis = 0; float best = 0f;
                for (int i = 0; i < 3; i++) { float d = Mathf.Abs(Vector3.Dot(axes[i], Vector3.up)); if (d > best) { best = d; upAxis = i; } }
                if (best < 0.94f) return false;
                float sign = Mathf.Sign(Vector3.Dot(axes[upAxis], Vector3.up));
                Vector3 localTop = b.center; localTop[upAxis] += b.extents[upAxis] * sign;
                int ax = (upAxis + 1) % 3, az = (upAxis + 2) % 3;
                Vector3 up = axes[upAxis] * sign, fwd = axes[az];
                s.Pose = new Pose(t.TransformPoint(localTop), Quaternion.LookRotation(fwd, up));
                // x must be the pose's right axis: right = up × forward
                Vector3 right = Vector3.Cross(up, fwd);
                float sx = Mathf.Abs(Vector3.Dot(right, axes[ax])) > 0.5f ? b.size[ax] : b.size[az];
                s.Size = new Vector2(sx * Mathf.Abs(t.lossyScale[ax]), b.size[az] * Mathf.Abs(t.lossyScale[az]));
            }
            else if (a.PlaneRect.HasValue)
            {
                if (Mathf.Abs(Vector3.Dot(t.forward, Vector3.up)) < 0.94f) return false;   // not a horizontal plane
                Rect r = a.PlaneRect.Value;
                Vector3 up = t.forward * Mathf.Sign(Vector3.Dot(t.forward, Vector3.up));
                s.Pose = new Pose(t.TransformPoint(new Vector3(r.center.x, r.center.y, 0)), Quaternion.LookRotation(t.up, up));
                s.Size = new Vector2(r.size.x, r.size.y);
            }
            else return false;
            s.Uuid = a.Anchor != OVRAnchor.Null ? a.Anchor.Uuid.ToString() : a.name + "@" + Vector3Int.RoundToInt(s.Pose.position * 10f);
            return true;
        }

        public void Walls(List<WallSurface> into)
        {
            if (!Available) return;
            foreach (var room in MRUK.Instance.Rooms)
            {
                Vector3 centre = room.FloorAnchor ? room.FloorAnchor.GetAnchorCenter() : room.transform.position;
                foreach (var a in room.WallAnchors)
                {
                    if (!a || !a.PlaneRect.HasValue) continue;
                    Transform t = a.transform;
                    Rect r = a.PlaneRect.Value;
                    Vector3 pos = t.TransformPoint(new Vector3(r.center.x, r.center.y, 0));
                    Vector3 n = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                    if (n.sqrMagnitude < 0.5f) continue;
                    if (Vector3.Dot(n, centre - pos) < 0f) n = -n;                           // always facing into the room
                    into.Add(new WallSurface { Pose = new Pose(pos, Quaternion.LookRotation(n, Vector3.up)), Size = r.size });
                }
            }
        }
    }
}
