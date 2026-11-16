using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// "Tonight, the star is you": velvet curtains and two small gilded balconies on the user's real wall,
    /// straight ahead of them. With no wall ahead (or no scene data) the same set frames the table instead.
    /// Everything is sized to stay inside the comfortable field of view (±20° horizontal, ±15° vertical).
    /// </summary>
    public sealed class WallCurtainStager : MonoBehaviour
    {
        IRoomSource _room;
        Transform _root, _left, _right, _valance, _boxes, _backdrop, _spot;
        float _shown;
        public bool OnWall { get; private set; }
        public Vector3 Centre => _root ? _root.position : transform.position;

        public static WallCurtainStager Create(Transform stage, IRoomSource room)
        {
            var go = new GameObject("WallCurtains");
            go.transform.SetParent(stage, false);
            var w = go.AddComponent<WallCurtainStager>();
            w._room = room;
            return w;
        }

        /// <summary>The wall section the user is facing: centre of the set and its size, or false to use the fallback frame.</summary>
        public static bool TryForwardWall(IRoomSource room, Pose head, out Pose set, out Vector2 size)
        {
            set = default; size = default;
            if (room == null || !room.Available) return false;
            var walls = new List<WallSurface>();
            room.Walls(walls);
            Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            float best = float.MaxValue;
            foreach (var w in walls)
            {
                Vector3 n = w.Pose.forward;
                float facing = Vector3.Dot(n, -f);
                if (facing < 0.5f) continue;                                   // side walls and the wall behind are never used
                float denom = Vector3.Dot(f, n);
                if (Mathf.Abs(denom) < 1e-3f) continue;
                float dist = Vector3.Dot(w.Pose.position - head.position, n) / denom;
                if (dist < 0.9f || dist > 6f) continue;
                Vector3 hit = head.position + f * dist;
                Vector3 local = Quaternion.Inverse(w.Pose.rotation) * (hit - w.Pose.position);
                if (Mathf.Abs(local.x) > w.Size.x * 0.5f) continue;            // the gaze ray misses this wall
                if (dist >= best) continue;
                best = dist;
                // keep the set inside the wall and inside the field of view at this distance
                // the drapes are scenery, not UI: they may fill a wider part of the view than captions do (the
                // essential part, the boxes and the spotlight, stays in the middle third)
                float width = Mathf.Min(w.Size.x, 2f * dist * Mathf.Tan(27f * Mathf.Deg2Rad), 3.2f);
                float height = Mathf.Min(w.Size.y, 2f * dist * Mathf.Tan(19f * Mathf.Deg2Rad), 2.2f);
                float x = Mathf.Clamp(local.x, -w.Size.x * 0.5f + width * 0.5f, w.Size.x * 0.5f - width * 0.5f);
                float y = Mathf.Clamp(local.y, -w.Size.y * 0.5f + height * 0.5f, w.Size.y * 0.5f - height * 0.5f);
                set = new Pose(w.Pose.position + w.Pose.rotation * new Vector3(x, y, 0) + n * 0.03f, Quaternion.LookRotation(-n, Vector3.up));
                size = new Vector2(width, height);
            }
            return best < float.MaxValue;
        }

        public IEnumerator Show(StageSite site, Pose head)
        {
            Pose set = default; Vector2 size = default;
            OnWall = site.Tier != 3 && TryForwardWall(_room, head, out set, out size);
            if (!OnWall)
            {
                // Fallback: a smaller frame standing behind the table, still straight ahead.
                size = new Vector2(0.86f, 0.56f);
                Vector3 pos = transform.parent.TransformPoint(new Vector3(0, size.y * 0.5f + 0.02f, 0.46f));
                set = new Pose(pos, transform.parent.rotation);
            }
            Rebuild(set, size);
            for (float t = 0; t < 1.4f; t += Time.deltaTime) { Apply(Mathf.SmoothStep(0, 1, t / 1.4f)); yield return null; }
            Apply(1f);
        }

        public IEnumerator Hide()
        {
            if (!_root) yield break;
            float from = _shown;
            for (float t = 0; t < 0.9f; t += Time.deltaTime) { Apply(Mathf.Lerp(from, 0f, Mathf.SmoothStep(0, 1, t / 0.9f))); yield return null; }
            HideImmediate();
        }

        public void HideImmediate() { _shown = 0f; if (_root) _root.gameObject.SetActive(false); }

        void Rebuild(Pose set, Vector2 size)
        {
            if (_root) Destroy(_root.gameObject);
            _root = new GameObject("Set").transform;
            _root.SetParent(transform, false);
            _root.SetPositionAndRotation(set.position, set.rotation);
            float w = size.x, h = size.y, top = h * 0.5f;
            // deep theatre red, mostly lit (an earlier pale-pink version read as a shower curtain on video)
            // (the velvet texture is already dark red; the rim term on steep folds is what washed it out to grey)
            var velvet = Mats.Lit("wallvelvet", "velvet", new Color(1.7f, 1.05f, 1.05f), clip: Mats.ClipNone, rim: 0.12f, unlit: 0.45f, twoSided: true, tiling: new Vector2(2f, 1f));
            var gold = Mats.Lit("wallgold", null, Mats.Gold, clip: Mats.ClipNone, rim: 1.1f);

            // A dark stage behind the opening, so the real wall's pictures and sockets do not show through the gap:
            // the wall becomes a proscenium. It drops with the drapes.
            var back = new MeshKit();
            back.Tint(Color.white).At(new Vector3(0, -h * 0.5f, 0.012f)).Card(w * 0.70f, h, new Vector2(0, 0), new Vector2(1.4f, 2f));
            _backdrop = back.ToObject("Backdrop", _root, Mats.Lit("wallback", "velvet", new Color(0.32f, 0.10f, 0.12f), clip: Mats.ClipNone, rim: 0f, unlit: 0.3f, tiling: new Vector2(2f, 1f))).transform;
            _backdrop.localPosition = new Vector3(0, top, 0);
            // a warm spotlight pool on that backdrop, behind the boxes
            var spot = new MeshKit();
            spot.Tint(Color.white).At(new Vector3(0, -h * 0.55f, -0.004f)).Card(w * 0.55f, h * 0.70f, Vector2.zero, Vector2.one);
            _spot = spot.ToObject("Spot", _backdrop, Mats.Fx("wallspot", new Color(1f, 0.74f, 0.42f, 0.42f), additive: true, soft: 1f)).transform;

            // Two tied-back drapes hanging from the top, and a scalloped valance with a gold fringe across it.
            var l = new MeshKit();
            l.Tint(Color.white).Drape(w * 0.46f, h, 6, w * 0.010f, 8, 0.50f, -w * 0.15f, 1.6f, 2f);
            _left = l.ToObject("DrapeL", _root, velvet).transform;
            _left.localPosition = new Vector3(-w * 0.28f, top, -0.004f);
            var r = new MeshKit();
            r.Tint(Color.white).Drape(w * 0.46f, h, 6, w * 0.010f, 8, 0.50f, w * 0.15f, 1.6f, 2f);
            _right = r.ToObject("DrapeR", _root, velvet).transform;
            _right.localPosition = new Vector3(w * 0.28f, top, -0.004f);

            var v = new MeshKit();
            v.Tint(new Color(0.9f, 0.9f, 0.9f)).At(new Vector3(0, 0, -w * 0.02f)).Drape(w * 1.04f, h * 0.16f, 9, w * 0.014f, 3, 0f, 0f, 3f, 0.4f);
            _valance = v.ToObject("Valance", _root, velvet).transform;
            _valance.localPosition = new Vector3(0, top, 0);
            var fr = new MeshKit();
            fr.Tint(Color.white).At(new Vector3(0, -h * 0.16f, -w * 0.022f)).Card(w * 1.04f, h * 0.025f, Vector2.zero, Vector2.one, true);
            fr.At(new Vector3(0, 0.004f, -w * 0.022f)).Card(w * 1.06f, h * 0.02f, Vector2.zero, Vector2.one, true);
            fr.ToObject("Fringe", _valance, gold);

            // Two little balconies with the audience in them, between the drapes.
            _boxes = new GameObject("Boxes").transform;
            _boxes.SetParent(_root, false);
            float bw = w * 0.26f, bh = bw * 0.36f;
            var front = Mats.Lit("wallbalcony", "balcony", new Color(1f, 0.9f, 0.8f), clip: Mats.ClipNone, rim: 0.5f);
            var crowd = Mats.Lit("wallaudience", "audience", Color.white, clip: Mats.ClipNone, unlit: 0.6f, cutoff: 0.4f, rim: 0f);
            for (int i = -1; i <= 1; i += 2)
            {
                var box = new GameObject("Box").transform;
                box.SetParent(_boxes, false);
                box.localPosition = new Vector3(i * w * 0.14f, -h * 0.12f, -w * 0.03f);
                var b = new MeshKit();
                b.Tint(Color.white).Card(bw, bh, Vector2.zero, new Vector2(1f, 1f));
                b.ToObject("Front", box, front);
                var a = new MeshKit();
                for (int p = 0; p < 4; p++)
                {
                    // four audience members per box from the 4 × 2 sprite sheet
                    int cell = (p + (i > 0 ? 4 : 0)) % 8;
                    Vector2 uv0 = new Vector2((cell % 4) * 0.25f, (cell / 4) * 0.5f);
                    a.Tint(Color.white).At(new Vector3((p - 1.5f) * bw * 0.23f, bh * 0.5f + bw * 0.105f, 0.006f))
                     .Card(bw * 0.25f, bw * 0.25f, uv0, uv0 + new Vector2(0.25f, 0.5f));
                }
                a.ToObject("Audience", box, crowd);
            }
            _root.gameObject.SetActive(true);
            Apply(0f);
        }

        // 0 = rolled up at the top, 1 = fully unfurled.
        void Apply(float x)
        {
            _shown = x;
            if (!_root) return;
            float drop = Mathf.Max(0.001f, x);
            _left.localScale = _right.localScale = new Vector3(1f, drop, 1f);
            _backdrop.localScale = new Vector3(1f, drop, 1f);
            _spot.gameObject.SetActive(x > 0.6f);
            _valance.localScale = new Vector3(Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(x * 2f)), Mathf.Clamp01(x * 3f), 1f);
            float b = Mathf.Clamp01((x - 0.55f) / 0.45f);
            _boxes.localScale = Vector3.one * Mathf.SmoothStep(0, 1, b);
            _boxes.gameObject.SetActive(b > 0.01f);
        }

        void Update()
        {
            // the audience bobs while it applauds
            if (_boxes && _boxes.gameObject.activeSelf)
                for (int i = 0; i < _boxes.childCount; i++)
                {
                    var a = _boxes.GetChild(i).Find("Audience");
                    if (a) a.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 7f + i * 1.7f)) * 0.006f, 0);
                }
        }
    }

    /// <summary>The closing title: "PARLOR" in a classic serif with the night's name in italic beneath, above the stage.</summary>
    public sealed class TitleCard : MonoBehaviour
    {
        TextMeshPro _title, _sub;
        Transform _panel;
        float _shown;

        public static TitleCard Create(Transform stage)
        {
            var go = new GameObject("TitleCard");
            go.transform.SetParent(stage, false);
            go.transform.localPosition = new Vector3(0, 0.20f, 0.14f);
            var t = go.AddComponent<TitleCard>();
            t.Build();
            return t;
        }

        TextMeshPro Text(string name, TMP_FontAsset font, float size, Color color, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, y, 0);
            var t = go.AddComponent<TextMeshPro>();
            if (font) t.font = font;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontSize = size; t.color = color;
            t.rectTransform.sizeDelta = new Vector2(0.5f, 0.08f);
            t.sortingOrder = 31;
            MeshKit.Quiet(t.GetComponent<Renderer>());
            return t;
        }

        void Build()
        {
            var p = new MeshKit();
            p.Card(0.40f, 0.15f, Vector2.zero, Vector2.one);
            _panel = p.ToObject("Panel", transform, Mats.Fx("titlepanel", new Color(0.16f, 0.02f, 0.05f, 0.86f), soft: 0.35f, queueOffset: 19)).transform;
            _panel.localPosition = new Vector3(0, 0, 0.003f);
            _title = Text("Title", Fonts.Title, 0.62f, Mats.Gold, 0.022f);
            _title.characterSpacing = 18f;
            _title.text = "PARLOR";
            _sub = Text("Subtitle", Fonts.Italic, 0.24f, Mats.Cream, -0.036f);
            if (Fonts.Italic == Fonts.Body) _sub.fontStyle = FontStyles.Italic;
            HideImmediate();
        }

        public IEnumerator Show(string subtitle)
        {
            _sub.text = subtitle;
            gameObject.SetActive(true);
            for (float t = 0; t < 0.7f; t += Time.deltaTime) { Apply(t / 0.7f); yield return null; }
            Apply(1f);
        }

        public IEnumerator Hide()
        {
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { Apply(1f - t / 0.5f); yield return null; }
            HideImmediate();
        }

        public void HideImmediate() { Apply(0f); gameObject.SetActive(false); }

        void Apply(float x)
        {
            _shown = Mathf.SmoothStep(0, 1, x);
            transform.localScale = new Vector3(1f, 1f, 1f) * Mathf.Lerp(0.85f, 1f, _shown);
            _title.alpha = _shown; _sub.alpha = Mathf.Clamp01(_shown * 1.5f - 0.5f);
            _panel.localScale = new Vector3(_shown, 1f, 1f);
        }
    }

    /// <summary>Night 2: the coin becomes a tiny bird in the user's palm. Built from primitives, like everything else.</summary>
    public sealed class BirdProp : MonoBehaviour
    {
        Transform _wingL, _wingR, _body;
        float _since;

        public static BirdProp Create(Transform stage)
        {
            var go = new GameObject("Bird");
            go.transform.SetParent(stage, false);
            var b = go.AddComponent<BirdProp>();
            b.Build();
            return b;
        }

        void Build()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            var gold = Mats.Lit("bird", null, new Color(1f, 0.83f, 0.42f), clip: Mats.ClipNone, rim: 1.0f);
            var k = new MeshKit();
            k.Tint(Color.white).At(new Vector3(0, 0.012f, 0), Quaternion.identity, new Vector3(0.8f, 0.8f, 1.25f)).Sphere(0.011f, 10, 6);
            k.At(new Vector3(0, 0.022f, 0.011f)).Sphere(0.0072f, 10, 6);
            k.Tint(new Color(1f, 0.55f, 0.2f)).At(new Vector3(0, 0.021f, 0.0175f), Quaternion.Euler(90, 0, 0)).Cylinder(0.0022f, 0.0002f, 0.007f, 6);
            k.Tint(Mats.Ink).At(new Vector3(0.0045f, 0.0245f, 0.0155f)).Sphere(0.0012f, 6, 4);
            k.At(new Vector3(-0.0045f, 0.0245f, 0.0155f)).Sphere(0.0012f, 6, 4);
            k.Tint(new Color(0.95f, 0.72f, 0.3f)).At(new Vector3(0, 0.013f, -0.017f), Quaternion.Euler(-70, 0, 0)).Cylinder(0.006f, 0.001f, 0.014f, 6);
            k.ToObject("Mesh", _body, gold);
            _wingL = Wing(-1f, gold); _wingR = Wing(1f, gold);
            gameObject.SetActive(false);
        }

        Transform Wing(float sign, Material mat)
        {
            var pivot = new GameObject(sign < 0 ? "WingL" : "WingR").transform;
            pivot.SetParent(_body, false);
            pivot.localPosition = new Vector3(sign * 0.007f, 0.015f, 0);
            var k = new MeshKit();
            k.Tint(new Color(1f, 0.9f, 0.6f)).At(new Vector3(sign * 0.010f, 0, -0.002f), Quaternion.Euler(90, 0, 0), new Vector3(1f, 0.6f, 1f))
             .Card(0.020f, 0.022f, Vector2.zero, Vector2.one, true);
            k.ToObject("Feather", pivot, mat);
            return pivot;
        }

        public void Show(Vector3 worldPos) { transform.position = worldPos; _since = 0f; gameObject.SetActive(true); }

        public void Follow(Vector3 worldPos, float t)
        {
            transform.position = Vector3.Lerp(transform.position, worldPos + Vector3.up * (Mathf.Abs(Mathf.Sin(t * 5f)) * 0.008f), 1f - Mathf.Exp(-22f * Time.deltaTime));
        }

        public void Hide() { gameObject.SetActive(false); }

        void Update()
        {
            _since += Time.deltaTime;
            float flap = Mathf.Sin(Time.time * 38f) * 48f;
            _wingL.localRotation = Quaternion.Euler(0, 0, -flap - 10f);
            _wingR.localRotation = Quaternion.Euler(0, 0, flap + 10f);
            _body.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 6f) * 10f, Mathf.Sin(Time.time * 1.7f) * 40f, 0);
            float s = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_since / 0.25f));
            transform.localScale = Vector3.one * s;
        }
    }
}
