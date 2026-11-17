using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// The Mishap's scenery: the room going dark, the giant shadow on the user's real wall, and the claw from the pit.
    /// Built from code like everything else; nothing here is loaded from disk.
    /// </summary>
    static class MishapArt
    {
        static Texture2D _shadow, _friend;

        /// <summary>The same silhouette without horns: his own shadow, once it has calmed down.</summary>
        public static Texture2D Friend => _friend ? _friend : _friend = Build(false, "FriendShadow");

        /// <summary>
        /// Procedural silhouette atlas, 512 × 512: left half the possessed magician (top hat with horns, cape), right
        /// half one clawed arm. Alpha only, soft edged. Built once, on first use (about 10 ms).
        /// </summary>
        public static Texture2D Shadow => _shadow ? _shadow : _shadow = Build(true, "MishapShadow");

        static Texture2D Build(bool horns, string name)
        {
            {
                const int W = 512, H = 512;
                var a = new float[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool body = x < 256;
                        float u = (body ? x : x - 256) / 255f, v = y / 511f;
                        a[y * W + x] = body ? Body(u, v, horns) : Arm(u, v);
                    }
                // soften: two box-blur passes
                for (int pass = 0; pass < 2; pass++)
                {
                    var b = new float[W * H];
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            float s = 0; int n = 0;
                            for (int dy = -2; dy <= 2; dy++)
                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= W || yy >= H || (xx < 256) != (x < 256)) continue;
                                    s += a[yy * W + xx]; n++;
                                }
                            b[y * W + x] = s / n;
                        }
                    a = b;
                }
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
                var px = new Color32[W * H];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a[i]) * 255));
                tex.SetPixels32(px);
                tex.Apply(false, true);
                return tex;
            }
        }

        static float Ellipse(float u, float v, float cu, float cv, float ru, float rv)
        {
            float du = (u - cu) / ru, dv = (v - cv) / rv;
            return du * du + dv * dv <= 1f ? 1f : 0f;
        }

        static float Body(float u, float v, bool horns)
        {
            float d = Mathf.Abs(u - 0.5f);
            // ragged cape, wide at the hem
            if (v < 0.58f)
            {
                float half = Mathf.Lerp(0.47f, 0.19f, v / 0.58f);
                float hem = 0.03f * Mathf.Abs(Mathf.Sin(u * 31f)) + 0.02f;
                if (d < half && v > hem) return 1f;
            }
            if (Ellipse(u, v, 0.5f, 0.565f, 0.25f, 0.06f) > 0) return 1f;          // shoulders
            if (Ellipse(u, v, 0.5f, 0.655f, 0.085f, 0.055f) > 0) return 1f;        // head
            if (v > 0.700f && v < 0.722f && d < 0.17f) return 1f;                  // hat brim
            if (v >= 0.722f && v < 0.88f && d < Mathf.Lerp(0.105f, 0.095f, (v - 0.722f) / 0.16f)) return 1f;   // crown
            // horns: curving out and up from the crown
            if (horns)
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i <= 24; i++)
                {
                    float t = i / 24f;
                    float hu = 0.5f + s * (0.10f + 0.16f * t + 0.03f * t * t), hv = 0.80f + 0.17f * t - 0.04f * t * t * t;
                    float r = Mathf.Lerp(0.032f, 0.004f, t);
                    if ((u - hu) * (u - hu) + (v - hv) * (v - hv) * 0.25f < r * r) return 1f;
                }
            return 0f;
        }

        // An arm rising from the shoulder (bottom centre) to a three-clawed hand at the top.
        static float Arm(float u, float v)
        {
            float d = Mathf.Abs(u - 0.5f);
            if (v < 0.70f && d < Mathf.Lerp(0.10f, 0.065f, v / 0.70f)) return 1f;
            if (Ellipse(u, v, 0.5f, 0.74f, 0.12f, 0.07f) > 0) return 1f;
            for (int f = -1; f <= 1; f++)
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    float cu = 0.5f + f * (0.07f + 0.16f * t) + 0.05f * t * t * f, cv = 0.78f + 0.20f * t - 0.05f * t * t;
                    float r = Mathf.Lerp(0.035f, 0.003f, t);
                    if ((u - cu) * (u - cu) + (v - cv) * (v - cv) < r * r) return 1f;
                }
            return 0f;
        }
    }

    /// <summary>
    /// Makes the user's real room go dark (and red) around the show. A dark, inside-out box around the head, drawn
    /// before anything else and alpha-blended over the passthrough underlay, so it darkens the real room on a headset
    /// and the synthetic room in the simulator alike, while every virtual object still draws on top at full
    /// brightness. Flicker never exceeds three changes a second (photosensitivity: under three flashes per second).
    /// </summary>
    public sealed class RoomDimmer : MonoBehaviour
    {
        IHeadSource _head;
        Material _mat;
        float _level, _target, _speed = 1.5f;
        Color _tint = new Color(0.10f, 0.0f, 0.02f);
        Coroutine _flicker;
        public float Level => _level;

        public static RoomDimmer Create(Transform parent, IHeadSource head)
        {
            var go = new GameObject("RoomDimmer");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<RoomDimmer>();
            d._head = head;
            var k = new MeshKit();
            k.Tint(Color.white).Box(new Vector3(7f, 7f, 7f), inside: true);
            d._mat = Mats.Fx("roomdimmer", new Color(0, 0, 0, 0));
            d._mat.renderQueue = 1000;   // background: before every opaque object, so it only darkens the room
            k.ToObject("Box", go.transform, d._mat);
            d.Apply();
            return d;
        }

        /// <summary>0 = the room as it is, 1 = nearly black. speed = level per second.</summary>
        public void SetLevel(float level, float speed = 1.5f, bool snap = false)
        {
            if (_flicker != null) { StopCoroutine(_flicker); _flicker = null; }
            _target = Mathf.Clamp01(level); _speed = speed;
            if (snap) { _level = _target; Apply(); }
        }

        public void SetTint(Color c) { _tint = c; Apply(); }

        /// <summary>The lights stutter between two levels, then settle on the last. At most three changes a second.</summary>
        public void Flicker(float seconds, float low, float high, float settle)
        {
            if (_flicker != null) StopCoroutine(_flicker);
            _flicker = StartCoroutine(FlickerRoutine(seconds, low, high, settle));
        }

        IEnumerator FlickerRoutine(float seconds, float low, float high, float settle)
        {
            bool up = true;
            for (float t = 0; t < seconds;)
            {
                _target = up ? high : low; _speed = 9f; up = !up;
                float step = Random.Range(0.34f, 0.55f);
                t += step;
                yield return new WaitForSeconds(step);
            }
            _target = settle; _speed = 2f;
            _flicker = null;
        }

        void Apply()
        {
            if (!_mat) return;
            var c = _tint; c.a = 0.90f * _level;
            _mat.SetColor("_Color", c);
            var r = GetComponentInChildren<Renderer>();
            if (r) r.enabled = _level > 0.003f;
        }

        void LateUpdate()
        {
            if (_head != null) transform.position = _head.Head.position;
            transform.rotation = Quaternion.identity;
            if (!Mathf.Approximately(_level, _target)) { _level = Mathf.MoveTowards(_level, _target, _speed * Time.deltaTime); Apply(); }
        }
    }

    /// <summary>
    /// The possessed magician's shadow, two metres tall, thrown on the real wall in front of the user (or, with no
    /// wall, standing behind the table). Black, alpha-blended over the passthrough, so it reads as a shadow on the
    /// wall itself. Its arms sway; it grows in, shudders when the user fights it, and is blown away by the Vanish.
    /// </summary>
    public sealed class WallShadow : MonoBehaviour
    {
        IRoomSource _room;
        Transform _root, _body, _armL, _armR, _eyes, _pool;
        Material _mat;
        Pose _wall; Vector2 _wallSize; bool _onWall;
        float _run, _dip;
        /// <summary>Where it ran off to (a real door or window, or the edge of the wall), once it has fled.</summary>
        public Vector3? ExitPoint { get; private set; }
        public bool OnWall => _onWall;
        public Vector3 Position => _root ? _root.position + Vector3.up * 0.6f : transform.position;
        float _grow, _growTarget, _shudder, _alpha = 0.82f, _spin;
        public bool Shown => _root && _root.gameObject.activeSelf;

        public static WallShadow Create(Transform stage, IRoomSource room)
        {
            var go = new GameObject("WallShadow");
            go.transform.SetParent(stage, false);
            var w = go.AddComponent<WallShadow>();
            w._room = room;
            return w;
        }

        void Build()
        {
            _root = new GameObject("Shadow").transform;
            _root.SetParent(transform, false);
            // a pool of hellish light on the wall first, so the black shadow inside it reads against a dark room
            var pool = new MeshKit();
            pool.At(new Vector3(0, 0.52f, 0.006f)).Card(1.25f, 1.25f, Vector2.zero, Vector2.one);
            _pool = pool.ToObject("Pool", _root, Mats.Fx("wallpool", new Color(1f, 0.32f, 0.10f, 0.55f), additive: true, soft: 1f, queueOffset: -3)).transform;
            _mat = Mats.Fx("wallshadow", new Color(0.02f, 0f, 0.03f, _alpha));
            _mat.SetTexture("_MainTex", MishapArt.Shadow);
            // body: unit height, pivot at the hem
            var b = new MeshKit();
            b.At(new Vector3(0, 0.5f, 0)).Card(0.5f, 1f, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            _body = b.ToObject("Body", _root, _mat).transform;
            Transform Arm(string name, float side)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(_root, false);
                pivot.localPosition = new Vector3(side * 0.20f, 0.555f, -0.002f);
                var k = new MeshKit();
                k.At(new Vector3(0, 0.27f, 0)).Card(0.27f, 0.54f, new Vector2(0.5f, 0f), new Vector2(1f, 1f));
                k.ToObject("Arm", pivot, _mat);
                return pivot;
            }
            _armL = Arm("ArmL", -1f); _armR = Arm("ArmR", 1f);
            // two red embers for eyes, on top of the shadow
            var e = new MeshKit();
            foreach (float s in new[] { -1f, 1f }) e.At(new Vector3(s * 0.022f, 0.663f, -0.004f)).Card(0.05f, 0.03f, Vector2.zero, Vector2.one);
            _eyes = e.ToObject("Eyes", _root, Mats.Fx("shadoweyes", new Color(1f, 0.12f, 0.05f, 0.95f), additive: true, soft: 1f)).transform;
            _root.gameObject.SetActive(false);
        }

        /// <summary>Places the shadow on the wall ahead (or behind the table) and grows it in over a couple of seconds.</summary>
        public void Show(StageSite site, Pose head)
        {
            if (!_root) Build();
            Pose set = default; Vector2 size = default;
            float height;
            _onWall = site.Tier != 3 && WallCurtainStager.TryForwardWall(_room, head, out set, out size);
            if (_onWall)
            {
                _wall = set; _wallSize = size;
                height = Mathf.Clamp(size.y * 1.05f, 1.2f, 2.1f);
                // stand the hem on the bottom of the visible wall section, slightly off the wall
                set.position += set.rotation * new Vector3(0, -size.y * 0.5f, -0.02f);
            }
            else
            {
                Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
                height = 1.4f;
                set = new Pose(head.position + f * 1.9f + Vector3.down * 0.9f, Quaternion.LookRotation(f, Vector3.up));
            }
            _root.SetPositionAndRotation(set.position, set.rotation);
            _root.localScale = Vector3.one * 0.001f;
            _baseScale = height;
            _grow = 0f; _growTarget = 1f; _shudder = 0f; _spin = 0f; _run = 0f; _dip = 0f;
            _mat.SetTexture("_MainTex", MishapArt.Shadow);
            _mat.color = new Color(0.02f, 0f, 0.03f, _alpha);
            _pool.gameObject.SetActive(true); _eyes.gameObject.SetActive(true);
            ExitPoint = null;
            _root.gameObject.SetActive(true);
        }

        /// <summary>
        /// The Vanish breaks its hold: it shrinks to an ordinary shadow and runs for it along the user's real wall, out
        /// through a real door or window in that wall if there is one (MRUK DOOR_FRAME / WINDOW_FRAME), otherwise off
        /// the edge of the wall. With no wall it simply spins away.
        /// </summary>
        public IEnumerator Flee()
        {
            if (!Shown) yield break;
            if (!_onWall) { yield return Banish(); yield break; }
            _eyes.gameObject.SetActive(false); _pool.gameObject.SetActive(false);
            _mat.SetTexture("_MainTex", MishapArt.Friend);
            float from = _baseScale, to = Mathf.Min(_baseScale, 0.95f);
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { _baseScale = Mathf.Lerp(from, to, t / 0.5f); _shudder = 1f - t / 0.5f; yield return null; }
            _shudder = 0f;

            // where to: an opening in this wall, else the nearer edge of the wall
            Vector3 n = -(_wall.rotation * Vector3.forward);                  // out of the wall, into the room
            Vector3 right = _wall.rotation * Vector3.right;
            float goal = float.NaN, best = float.MaxValue;
            var openings = new List<WallSurface>();
            _room?.Openings(openings);
            foreach (var o in openings)
            {
                if (Vector3.Dot(o.Pose.forward, n) < 0.85f) continue;          // another wall
                if (Mathf.Abs(Vector3.Dot(o.Pose.position - _root.position, n)) > 0.25f) continue;
                float x = Vector3.Dot(o.Pose.position - _root.position, right);
                if (Mathf.Abs(x) < best) { best = Mathf.Abs(x); goal = x; }
            }
            bool door = !float.IsNaN(goal);
            if (!door)
            {
                float here = Vector3.Dot(_root.position - _wall.position, right);
                float edge = _wallSize.x * 0.5f + 0.5f;
                goal = (here >= 0f ? edge : -edge) - here;
            }
            Vector3 start = _root.position, end = start + right * goal;
            float dur = Mathf.Clamp(Mathf.Abs(goal) / 1.6f, 0.8f, 2.4f);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float x = Mathf.SmoothStep(0f, 1f, t / dur);
                _run = Mathf.Sign(goal);
                _root.position = Vector3.Lerp(start, end, x) + Vector3.up * (Mathf.Abs(Mathf.Sin(t * 14f)) * 0.04f);
                // through a door it shrinks into the opening; off a wall's edge it just leaves
                if (door && x > 0.8f) _baseScale = Mathf.Lerp(to, 0.05f, (x - 0.8f) / 0.2f);
                yield return null;
            }
            ExitPoint = end;
            _run = 0f;
            HideImmediate();
        }

        /// <summary>Comes back, calm and hornless, from wherever it fled, and stands at the given spot on the wall.</summary>
        public IEnumerator ReturnAsFriend(Pose spot, float height)
        {
            if (!_root) Build();
            _mat.SetTexture("_MainTex", MishapArt.Friend);
            _mat.color = new Color(0.02f, 0f, 0.03f, 0.62f);
            _pool.gameObject.SetActive(false); _eyes.gameObject.SetActive(false);
            Vector3 from = ExitPoint ?? spot.position + spot.rotation * Vector3.right * 1.2f;
            _root.SetPositionAndRotation(from, spot.rotation);
            _baseScale = height; _grow = 1f; _growTarget = 1f; _spin = 0f; _shudder = 0f; _dip = 0f;
            _root.gameObject.SetActive(true);
            float dist = (spot.position - from).magnitude;
            float dur = Mathf.Clamp(dist / 1.0f, 0.6f, 2.5f);
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float x = Mathf.SmoothStep(0f, 1f, t / dur);
                _run = Mathf.Sign(Vector3.Dot(spot.position - from, spot.rotation * Vector3.right)) * 0.5f;   // a shy walk, not a run
                _root.position = Vector3.Lerp(from, spot.position, x) + Vector3.up * (Mathf.Abs(Mathf.Sin(t * 8f)) * 0.025f);
                yield return null;
            }
            _run = 0f;
            _root.position = spot.position;
        }

        /// <summary>A bow on the wall: it dips down and up again.</summary>
        public IEnumerator Bow()
        {
            if (!Shown) yield break;
            for (float t = 0; t < 1.3f; t += Time.deltaTime) { _dip = Mathf.Sin(Mathf.Clamp01(t / 1.3f) * Mathf.PI); yield return null; }
            _dip = 0f;
        }

        float _baseScale = 1.6f;

        public void Shudder(float amount) { _shudder = Mathf.Clamp01(amount); }

        /// <summary>The Vanish blows it away: it spins and shrinks to nothing.</summary>
        public IEnumerator Banish()
        {
            if (!Shown) yield break;
            _growTarget = 0f;
            for (float t = 0; t < 0.8f; t += Time.deltaTime) { _spin += Time.deltaTime * 900f * (1f - t); yield return null; }
            HideImmediate();
        }

        public void HideImmediate() { if (_root) _root.gameObject.SetActive(false); _grow = 0f; _growTarget = 0f; }

        void Update()
        {
            if (!Shown) return;
            float t = Time.time;
            _grow = Mathf.MoveTowards(_grow, _growTarget, Time.deltaTime * (_growTarget > _grow ? 0.55f : 1.6f));
            float g = Mathf.SmoothStep(0f, 1f, _grow);
            float jitter = _shudder * 0.02f;
            _root.localScale = new Vector3(1f + Random.Range(-jitter, jitter), (1f + Random.Range(-jitter, jitter)) * (1f - 0.2f * _dip), 1f) * (_baseScale * Mathf.Max(0.001f, g));
            float lean = -_run * 12f;                                         // leaning into the run
            _body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.9f) * 3f + _spin + lean);
            if (_run != 0f)
            {
                // running: arms pumping
                _armL.localRotation = Quaternion.Euler(0, 0, 60f + Mathf.Sin(t * 14f) * 40f + lean);
                _armR.localRotation = Quaternion.Euler(0, 0, -60f - Mathf.Sin(t * 14f) * 40f + lean);
            }
            else if (_dip > 0f)
            {
                // bowing: one arm sweeps across, one out
                _armL.localRotation = Quaternion.Euler(0, 0, 150f * _dip + 20f);
                _armR.localRotation = Quaternion.Euler(0, 0, -100f * _dip - 20f);
            }
            else
            {
                _armL.localRotation = Quaternion.Euler(0, 0, 38f + Mathf.Sin(t * 1.3f) * 14f + _spin);
                _armR.localRotation = Quaternion.Euler(0, 0, -38f - Mathf.Sin(t * 1.3f + 1.1f) * 14f + _spin);
            }
            _eyes.localScale = Vector3.one * (0.85f + 0.2f * Mathf.Sin(t * 3.1f));
        }
    }

    /// <summary>
    /// The thing under the table: a long, dark, clawed arm. Its root is the wrist; the arm trails back to an anchor
    /// point deep in the pit, so it always appears to come up out of the trapdoor (below the table it is only
    /// visible through the opening).
    /// </summary>
    public sealed class ShadowClaw : MonoBehaviour
    {
        readonly Transform[] _fingers = new Transform[4];
        readonly float[] _yaw = { 0f, 100f, 200f, 290f };
        float _grip;
        public Transform Palm { get; private set; }

        public static ShadowClaw Create(Transform parent)
        {
            var go = new GameObject("Claw");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ShadowClaw>();
            c.Build();
            go.transform.localScale = Vector3.one * 1.5f;   // big enough to read as a monster's hand next to a 24 cm magician
            go.SetActive(false);
            return c;
        }

        void Build()
        {
            var mat = Mats.Lit("claw", null, new Color(0.22f, 0.11f, 0.28f), clip: Mats.ClipBelow, rim: 0.55f);
            var k = new MeshKit();
            k.Tint(Color.white).At(Vector3.down * 0.70f).Cylinder(0.017f, 0.012f, 0.70f, 8, false, false);   // arm
            k.At(Vector3.down * 0.004f, Quaternion.identity, new Vector3(0.016f, 0.010f, 0.016f)).Sphere(1f, 8, 6);   // wrist knob
            k.At(new Vector3(0, 0.012f, 0), Quaternion.identity, new Vector3(0.022f, 0.015f, 0.022f)).Sphere(1f, 10, 8); // hand
            k.ToObject("Arm", transform, mat);
            for (int i = 0; i < 4; i++)
            {
                var pivot = new GameObject("Finger" + i).transform;
                pivot.SetParent(transform, false);
                var f = new MeshKit();
                bool thumb = i == 3;
                f.Tint(Color.white).Cylinder(thumb ? 0.006f : 0.0055f, 0.0012f, thumb ? 0.034f : 0.052f, 6, false, true);
                f.ToObject("Mesh", pivot, mat);
                _fingers[i] = pivot;
            }
            Palm = new GameObject("Palm").transform;
            Palm.SetParent(transform, false);
            Palm.localPosition = new Vector3(0, 0.03f, 0);
            SetGrip(0f);
        }

        /// <summary>0 = fingers splayed, 1 = clenched.</summary>
        public void SetGrip(float g) { _grip = Mathf.Clamp01(g); ApplyFingers(0f); }

        void ApplyFingers(float wiggle)
        {
            for (int i = 0; i < 4; i++)
            {
                Quaternion yaw = Quaternion.AngleAxis(_yaw[i], Vector3.up);
                float w = i < 3 ? Mathf.Sin(Time.time * 7f + i * 2f) * wiggle : 0f;
                _fingers[i].localPosition = yaw * new Vector3(0, 0.016f, 0.017f);
                _fingers[i].localRotation = yaw * Quaternion.Euler(Mathf.Lerp(32f, -62f, _grip) + w, 0, 0);
            }
        }

        /// <summary>Wrist at a world point, arm trailing back to the anchor.</summary>
        public void Pose(Vector3 wrist, Vector3 anchor)
        {
            transform.position = wrist;
            Vector3 d = anchor - wrist;
            transform.rotation = d.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(Vector3.down, d.normalized) : Quaternion.identity;
        }

        public IEnumerator Move(Vector3 from, Vector3 to, Vector3 anchor, float seconds)
        {
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                Pose(Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)), anchor);
                yield return null;
            }
            Pose(to, anchor);
        }

        void Update()
        {
            // the fingers never quite keep still
            ApplyFingers(_grip < 0.5f ? 12f : 0f);
        }
    }
}
