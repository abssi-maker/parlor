using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// For the curtain call the user's whole room becomes the theatre: every scanned wall (MRUK WALL_FACE) takes on
    /// the Parlor's damask wallpaper, a gilded dado rail and a row of glowing sconces, faded in over the dimmed
    /// passthrough like stage lighting thrown onto the real walls, and faded out again after the bows. It is drawn
    /// half-transparent, so the user's own furniture still shows through: their room, dressed for the show.
    /// With no scene data (floating-table fallback) it does nothing.
    /// </summary>
    public sealed class RoomTheatre : MonoBehaviour
    {
        IRoomSource _room;
        Transform _root;
        Material _paper, _rail, _glow;
        float _shown, _target;
        public bool Built => _root;

        const float PaperAlpha = 0.5f, RailAlpha = 0.8f, GlowAlpha = 0.75f;

        public static RoomTheatre Create(Transform stage, IRoomSource room)
        {
            var go = new GameObject("RoomTheatre");
            go.transform.SetParent(stage, false);
            var t = go.AddComponent<RoomTheatre>();
            t._room = room;
            return t;
        }

        /// <summary>Starts fading the room in. Returns false when there are no walls to dress.</summary>
        public bool Show(StageSite site)
        {
            if (site.Tier == 3 || _room == null || !_room.Available) return false;
            if (!_root && !Build()) return false;
            _root.gameObject.SetActive(true);
            _target = 1f;
            return true;
        }

        public void Hide() { _target = 0f; }

        public void HideImmediate() { _target = 0f; _shown = 0f; Apply(); if (_root) _root.gameObject.SetActive(false); }

        bool Build()
        {
            var walls = new List<WallSurface>();
            _room.Walls(walls);
            if (walls.Count == 0) return false;
            _root = new GameObject("Walls").transform;
            _root.SetParent(transform, false);
            // world-space set: the stage may be rotated, the room is not
            _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _root.localScale = Vector3.one;

            _paper = Mats.Fx("roompaper", new Color(1.25f, 0.82f, 0.62f, 0f), texture: "wallpaper", queueOffset: -2);
            _rail = Mats.Fx("roomrail", new Color(Mats.Gold.r, Mats.Gold.g, Mats.Gold.b, 0f), queueOffset: -1);
            _glow = Mats.Fx("roomglow", new Color(1f, 0.72f, 0.40f, 0f), additive: true, soft: 1f);

            float floorY = _room.TryFloorY(out float fy) ? fy : float.NaN;
            foreach (var w in walls)
            {
                Vector3 n = w.Pose.forward;                                  // into the room
                var wall = new GameObject("Wall").transform;
                wall.SetParent(_root, false);
                // cards face -Z: look into the wall
                wall.SetPositionAndRotation(w.Pose.position + n * 0.015f, Quaternion.LookRotation(-n, Vector3.up));
                float width = w.Size.x, height = w.Size.y;
                float bottom = float.IsNaN(floorY) ? -height * 0.5f : Mathf.Clamp(floorY - w.Pose.position.y, -height * 0.5f, 0f);

                var paper = new MeshKit();
                paper.Tint(Color.white).Card(width, height, Vector2.zero, new Vector2(width / 0.55f, height / 0.55f));
                paper.ToObject("Paper", wall, _paper);

                // a gilded dado rail at 0.95 m and a picture rail near the top
                var rail = new MeshKit();
                rail.Tint(Color.white).At(new Vector3(0, bottom + 0.95f, -0.004f)).Card(width, 0.035f, Vector2.zero, Vector2.one);
                rail.At(new Vector3(0, height * 0.5f - 0.18f, -0.004f)).Card(width, 0.025f, Vector2.zero, Vector2.one);
                rail.ToObject("Rails", wall, _rail);

                // sconces every 1.3 m at 1.75 m: a pool of warm light on the wall around a small bright flame
                var glow = new MeshKit();
                int count = Mathf.Max(1, Mathf.FloorToInt(width / 1.3f));
                for (int i = 0; i < count; i++)
                {
                    float x = (i + 0.5f) / count * width - width * 0.5f;
                    float y = Mathf.Min(bottom + 1.75f, height * 0.5f - 0.3f);
                    glow.Tint(Color.white).At(new Vector3(x, y, -0.006f)).Card(0.75f, 0.75f, Vector2.zero, Vector2.one);
                    glow.Tint(new Color(1f, 1f, 1f, 1f)).At(new Vector3(x, y, -0.008f)).Card(0.10f, 0.14f, Vector2.zero, Vector2.one);
                }
                glow.ToObject("Sconces", wall, _glow);
            }
            Apply();
            _root.gameObject.SetActive(false);
            return true;
        }

        void Update()
        {
            if (!_root || !_root.gameObject.activeSelf) return;
            _shown = Mathf.MoveTowards(_shown, _target, Time.deltaTime * (_target > _shown ? 0.5f : 0.9f));
            Apply();
            if (_shown <= 0f && _target <= 0f) _root.gameObject.SetActive(false);
        }

        void Apply()
        {
            if (!_paper) return;
            float x = Mathf.SmoothStep(0f, 1f, _shown);
            // candlelight: a slow, gentle breathing of the sconces (far under three changes a second)
            float flicker = 0.92f + 0.08f * Mathf.Sin(Time.time * 2.1f) * Mathf.Sin(Time.time * 0.7f + 1f);
            SetAlpha(_paper, PaperAlpha * x);
            SetAlpha(_rail, RailAlpha * x);
            SetAlpha(_glow, GlowAlpha * x * flicker);
        }

        static void SetAlpha(Material m, float a) { var c = m.color; c.a = a; m.color = c; }
    }
}
