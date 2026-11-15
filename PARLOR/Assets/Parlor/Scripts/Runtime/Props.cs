using System.Collections;
using UnityEngine;

namespace Parlor
{
    /// <summary>Where things sit, in stage space. One place to tune reach and field of view.</summary>
    /// <remarks>
    /// The seated user's eyes are expected near (0, 0.40, -0.57). Everything the user must touch is within a
    /// two-foot (0.61 m) reach of that point, and the whole cluster spans about ±18° horizontally and ±15°
    /// vertically around the line of sight to the magician, so it also fits the narrower Glasses field of view.
    /// </remarks>
    public static class Layout
    {
        public static readonly Vector3 CoinLanding = new Vector3(-0.105f, 0f, -0.195f);
        public static readonly Vector3 Case = new Vector3(0.125f, 0f, -0.205f);
        public static readonly Vector3 CardRowCentre = new Vector3(0f, 0.145f, 0.13f);
        public const float CardSpacing = 0.165f;
        public static readonly Vector3 Caption = new Vector3(0f, 0.228f, 0.13f);   // just above the card row and the magician, in the same glance
        public static readonly Vector3 Encore = new Vector3(0f, 0.12f, -0.17f);
        public static readonly Vector3 ExpectedEye = new Vector3(0f, 0.40f, -0.57f);
        public const float Reach = 0.61f;
    }

    /// <summary>The borrowed coin: gold, fleur-de-lis on both faces, with a contact shadow that sells "resting on the table".</summary>
    public sealed class CoinProp : MonoBehaviour
    {
        public const float Radius = 0.0165f, Thickness = 0.0028f;
        /// <summary>Grab volume: more than three times the visual size, by design.</summary>
        public const float GrabRadius = 0.065f;

        Transform _shadow, _halo;
        Renderer _shadowR, _haloR, _body;
        MaterialPropertyBlock _mpb;
        Transform _stage;
        float _glow, _glowTarget;
        public float Spin;      // degrees per second around its own up axis (visual only)
        public Transform Visual { get; private set; }

        public static CoinProp Create(Transform stage)
        {
            var go = new GameObject("Coin");
            go.transform.SetParent(stage, false);
            var c = go.AddComponent<CoinProp>();
            c._stage = stage;
            c.Build();
            return c;
        }

        void Build()
        {
            _mpb = new MaterialPropertyBlock();
            Visual = new GameObject("Visual").transform;
            Visual.SetParent(transform, false);

            var faces = new MeshKit();
            faces.Tint(Color.white).At(Vector3.up * (Thickness * 0.5f)).Disc(Radius, 24, 0f, true);
            faces.At(Vector3.down * (Thickness * 0.5f)).Disc(Radius, 24, 0f, false);
            _body = faces.ToObject("Faces", Visual, Mats.Lit("coin", "coin", new Color(1.0f, 0.86f, 0.52f), rim: 1.1f)).GetComponent<Renderer>();

            var edge = new MeshKit();
            edge.Tint(Mats.Gold).At(Vector3.down * (Thickness * 0.5f)).Cylinder(Radius, Radius, Thickness, 24, false, false);
            edge.ToObject("Edge", Visual, Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f));

            // contact shadow lives on the table plane, not on the coin
            var s = new MeshKit();
            s.Ground(1f, 1f);
            _shadow = s.ToObject("CoinShadow", _stage, Mats.Fx("shadow", new Color(0.05f, 0.02f, 0.0f, 0.55f), soft: 0.9f, clip: Mats.ClipHole, queueOffset: -5)).transform;
            _shadowR = _shadow.GetComponent<Renderer>();

            var h = new MeshKit();
            h.Card(1f, 1f, Vector2.zero, Vector2.one, true);
            _halo = h.ToObject("CoinHalo", transform, Mats.Fx("coinhalo", new Color(1f, 0.82f, 0.45f, 0f), additive: true, soft: 1f, clip: Mats.ClipBelow)).transform;
            _halo.localScale = Vector3.one * 0.11f;
            _haloR = _halo.GetComponent<Renderer>();
        }

        /// <summary>Hover feedback: the coin glints when a hand is close enough to take it.</summary>
        public void SetGlow(float level) { _glowTarget = Mathf.Clamp01(level); }

        public void SetVisible(bool on)
        {
            Visual.gameObject.SetActive(on);
            if (!on) { _shadowR.enabled = false; _haloR.enabled = false; }
        }

        public bool IsVisible => Visual.gameObject.activeSelf;

        void LateUpdate()
        {
            if (!Visual.gameObject.activeSelf) return;
            if (Spin != 0f) Visual.Rotate(Vector3.up, Spin * Time.deltaTime, Space.Self);

            // shadow: directly under the coin on the table plane; tighter and darker the closer it is
            Vector3 p = _stage.InverseTransformPoint(transform.position);
            float h = p.y;
            bool show = h > -0.002f && h < 0.25f;
            _shadowR.enabled = show;
            if (show)
            {
                _shadow.localPosition = new Vector3(p.x, 0.0007f, p.z);
                float spread = Mathf.Lerp(2.6f, 5.5f, Mathf.Clamp01(h / 0.2f));
                _shadow.localScale = new Vector3(Radius * spread, 1f, Radius * spread);
                Color c = _shadowR.sharedMaterial.GetColor("_Color");
                c.a = Mathf.Lerp(0.62f, 0.10f, Mathf.Clamp01(h / 0.2f));
                _shadowR.GetPropertyBlock(_mpb); _mpb.SetColor("_Color", c); _shadowR.SetPropertyBlock(_mpb);
            }

            _glow = Mathf.MoveTowards(_glow, _glowTarget, Time.deltaTime * 4f);
            _haloR.enabled = _glow > 0.01f;
            if (_haloR.enabled)
            {
                var cam = Camera.main;
                if (cam) _halo.rotation = Quaternion.LookRotation(_halo.position - cam.transform.position);
                Color c = _haloR.sharedMaterial.GetColor("_Color");
                c.a = _glow * (0.45f + 0.15f * Mathf.Sin(Time.time * 6f));
                _haloR.GetPropertyBlock(_mpb); _mpb.SetColor("_Color", c); _haloR.SetPropertyBlock(_mpb);
            }
        }

        void OnDestroy() { if (_shadow) Destroy(_shadow.gameObject); }
    }

    /// <summary>A playing card: face from the card atlas (3x3), ornate back. Large enough to poke comfortably.</summary>
    public sealed class CardProp : MonoBehaviour
    {
        public const float Width = 0.078f, Height = 0.109f;
        public const float PokeRadius = 0.062f;   // ≥ 4 cm target, with slack
        public int Face { get; private set; }
        Renderer _face;

        public static CardProp Create(Transform parent, int face)
        {
            var go = new GameObject("Card" + face);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<CardProp>();
            c.Face = face;
            c.Build();
            return c;
        }

        /// <summary>UV rectangle of a face in the 3x3 atlas (row 0 at the top of the image).</summary>
        public static void FaceUv(int face, out Vector2 min, out Vector2 max)
        {
            int col = face % 3, row = face / 3;
            const float pad = 0.004f;
            min = new Vector2(col / 3f + pad, 1f - (row + 1) / 3f + pad);
            max = new Vector2((col + 1) / 3f - pad, 1f - row / 3f - pad);
        }

        void Build()
        {
            FaceUv(Face, out var uvMin, out var uvMax);
            // The atlas cells are square and a card is portrait: the pictogram keeps its proportions on a square in the
            // middle of a cream card with a gold border, so it is never stretched.
            var paper = new MeshKit();
            paper.Tint(Mats.Gold).Card(Width, Height, Vector2.zero, Vector2.one);
            paper.Tint(Mats.Cream).At(new Vector3(0, 0, -0.0002f)).Card(Width - 0.005f, Height - 0.005f, Vector2.zero, Vector2.one);
            paper.ToObject("Paper", transform, Mats.Lit("cardpaper", null, Color.white, rim: 0.05f, unlit: 0.35f));
            var f = new MeshKit();
            f.Tint(Color.white).At(new Vector3(0, 0, -0.0004f)).Card(Width - 0.008f, Width - 0.008f, uvMin, uvMax);
            _face = f.ToObject("Face", transform, Mats.Lit("cards", "cards", Color.white, rim: 0.05f, unlit: 0.35f)).GetComponent<Renderer>();

            var b = new MeshKit();
            b.Tint(Color.white).At(new Vector3(0, 0, 0.0006f), Quaternion.Euler(0, 180, 0)).Card(Width, Height, Vector2.zero, Vector2.one);
            b.ToObject("Back", transform, Mats.Lit("cardback", "cardback", Color.white, rim: 0.05f, unlit: 0.25f));
        }
    }

    /// <summary>The burgundy velvet case with brass clasp. The lid is hinged at the back.</summary>
    public sealed class VelvetCase : MonoBehaviour
    {
        public static readonly Vector3 Size = new Vector3(0.104f, 0.034f, 0.064f);
        public const float CoverRadius = 0.11f, CoverHeight = 0.16f;
        Transform _lid;
        float _open, _openTarget;

        public static VelvetCase Create(Transform stage)
        {
            var go = new GameObject("VelvetCase");
            go.transform.SetParent(stage, false);
            var c = go.AddComponent<VelvetCase>();
            c.Build(stage);
            return c;
        }

        void Build(Transform stage)
        {
            float baseH = Size.y * 0.62f, lidH = Size.y * 0.38f;
            var velvet = Mats.Lit("casevelvet", "velvet", new Color(0.72f, 0.30f, 0.36f), rim: 0.9f, tiling: new Vector2(0.25f, 0.25f));
            var gold = Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f);

            var b = new MeshKit();
            b.Tint(Color.white).At(Vector3.up * (baseH * 0.5f)).Box(new Vector3(Size.x, baseH, Size.z), false, 4f);
            b.ToObject("Base", transform, velvet);

            _lid = new GameObject("Lid").transform;
            _lid.SetParent(transform, false);
            _lid.localPosition = new Vector3(0, baseH, Size.z * 0.5f);     // hinge line at the back edge
            var l = new MeshKit();
            l.Tint(Color.white).At(new Vector3(0, lidH * 0.5f, -Size.z * 0.5f)).Box(new Vector3(Size.x, lidH, Size.z), false, 4f);
            l.ToObject("LidMesh", _lid, velvet);

            var g = new MeshKit();
            g.Tint(Mats.Gold);
            // brass corner caps on the base and edge band
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    g.At(new Vector3(sx * (Size.x * 0.5f - 0.004f), baseH * 0.5f, sz * (Size.z * 0.5f - 0.004f))).Box(new Vector3(0.0105f, baseH + 0.001f, 0.0105f));
            g.At(new Vector3(0, baseH - 0.0015f, 0)).Box(new Vector3(Size.x + 0.002f, 0.003f, Size.z + 0.002f));
            g.ToObject("Trim", transform, gold);

            var c = new MeshKit();
            c.Tint(Mats.Gold);
            // clasp on the lid's front face, and lid corner caps
            c.At(new Vector3(0, 0.001f, -Size.z - 0.0012f)).Box(new Vector3(0.018f, 0.014f, 0.003f));
            c.Tint(Mats.GoldDark).At(new Vector3(0, -0.001f, -Size.z - 0.003f), Quaternion.identity, new Vector3(0.004f, 0.004f, 0.002f)).Sphere(1f, 6, 4);
            c.Tint(Mats.Gold);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { 0f, 1f })
                    c.At(new Vector3(sx * (Size.x * 0.5f - 0.004f), lidH * 0.5f, -sz * (Size.z - 0.008f) - 0.004f)).Box(new Vector3(0.0105f, lidH + 0.001f, 0.0105f));
            c.ToObject("LidTrim", _lid, gold);

            // dark velvet interior, visible when the lid is open
            var i = new MeshKit();
            i.Tint(new Color(0.20f, 0.02f, 0.05f)).At(Vector3.up * (baseH + 0.0004f)).Ground(Size.x - 0.012f, Size.z - 0.012f);
            i.ToObject("Interior", transform, velvet);

            // contact shadow
            var s = new MeshKit();
            s.At(Vector3.up * 0.0006f).Ground(Size.x * 1.7f, Size.z * 1.9f);
            s.ToObject("CaseShadow", transform, Mats.Fx("shadow", new Color(0.05f, 0.02f, 0.0f, 0.55f), soft: 0.9f, clip: Mats.ClipHole, queueOffset: -5));
        }

        public void SetOpen(bool open) { _openTarget = open ? 1f : 0f; }
        /// <summary>World-space point a covering hand should hover over.</summary>
        public Vector3 CoverPoint => transform.TransformPoint(new Vector3(0, Size.y, 0));
        public Vector3 Mouth => transform.TransformPoint(new Vector3(0, Size.y * 0.7f, 0));

        void Update()
        {
            _open = Mathf.Lerp(_open, _openTarget, 1f - Mathf.Exp(-9f * Time.deltaTime));
            _lid.localRotation = Quaternion.Euler(-105f * _open, 0, 0);
        }
    }

    /// <summary>The floating encore coin: poke to replay. Also carries the hidden debug gesture.</summary>
    public sealed class EncoreToken : MonoBehaviour
    {
        public const float Radius = 0.034f, PokeRadius = 0.06f;
        Transform _spin;
        float _shown;
        public bool Shown;

        public static EncoreToken Create(Transform stage)
        {
            var go = new GameObject("EncoreToken");
            go.transform.SetParent(stage, false);
            var t = go.AddComponent<EncoreToken>();
            t.Build();
            return t;
        }

        void Build()
        {
            _spin = new GameObject("Spin").transform;
            _spin.SetParent(transform, false);
            var k = new MeshKit();
            // an upright coin: faces look along ±Z
            k.Tint(Color.white).At(new Vector3(0, 0, -0.003f), Quaternion.Euler(-90, 0, 0)).Disc(Radius, 28, 0f, true);
            k.At(new Vector3(0, 0, 0.003f), Quaternion.Euler(90, 0, 0)).Disc(Radius, 28, 0f, true);
            k.ToObject("Faces", _spin, Mats.Lit("coin", "coin", new Color(1.0f, 0.86f, 0.52f), rim: 1.1f));
            var e = new MeshKit();
            e.Tint(Mats.Gold).At(new Vector3(0, 0, -0.003f), Quaternion.Euler(90, 0, 0)).Cylinder(Radius, Radius, 0.006f, 28, false, false);
            e.ToObject("Edge", _spin, Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f));
            var h = new MeshKit();
            h.Card(0.2f, 0.2f, Vector2.zero, Vector2.one, true);
            h.ToObject("Halo", transform, Mats.Fx("tokenhalo", new Color(1f, 0.80f, 0.42f, 0.40f), additive: true, soft: 1f));
            transform.localScale = Vector3.zero;
        }

        void Update()
        {
            _shown = Mathf.MoveTowards(_shown, Shown ? 1f : 0f, Time.deltaTime * 2.5f);
            float s = Mathf.SmoothStep(0, 1, _shown);
            transform.localScale = Vector3.one * s;
            _spin.localRotation = Quaternion.Euler(0, Time.time * 70f, 0);
            _spin.localPosition = new Vector3(0, Mathf.Sin(Time.time * 1.6f) * 0.006f, 0);
        }
    }

    /// <summary>Particle effects, configured entirely in code. All use the additive Parlor/Fx material.</summary>
    public static class Sparkles
    {
        public static ParticleSystem Create(Transform parent, string name, Color color, float size, float life, int max, bool portalClipped = true, float gravity = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.4f, size);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.012f;

            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(0.2f, 1f), new Keyframe(1, 0.1f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mats.Fx(portalClipped ? "sparkle" : "sparkle_free", Color.white, additive: true, soft: 1f,
                                       clip: portalClipped ? Mats.ClipBelow : Mats.ClipNone, queueOffset: 5);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.enableGPUInstancing = false;
            MeshKit.Quiet(r);
            return ps;
        }

        public static void Burst(ParticleSystem ps, Vector3 worldPos, int count, float speed = 0.12f)
        {
            ps.transform.position = worldPos;
            var main = ps.main; main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            ps.Emit(count);
            if (!ps.isPlaying) ps.Play();
        }

        public static void Trail(ParticleSystem ps, bool on, float rate = 60f)
        {
            var em = ps.emission; em.rateOverTime = on ? rate : 0f;
            if (on && !ps.isPlaying) ps.Play();
        }
    }

    /// <summary>Tiny tweening helpers for scripted prop motion.</summary>
    public static class Tween
    {
        public static IEnumerator Move(Transform t, Vector3 toLocal, float seconds, float arc = 0f)
        {
            Vector3 from = t.localPosition;
            for (float e = 0; e < seconds; e += Time.deltaTime)
            {
                float x = Mathf.SmoothStep(0, 1, e / seconds);
                t.localPosition = Vector3.Lerp(from, toLocal, x) + Vector3.up * (arc * 4f * x * (1f - x));
                yield return null;
            }
            t.localPosition = toLocal;
        }

        public static IEnumerator Rotate(Transform t, Quaternion toLocal, float seconds)
        {
            Quaternion from = t.localRotation;
            for (float e = 0; e < seconds; e += Time.deltaTime)
            {
                t.localRotation = Quaternion.Slerp(from, toLocal, Mathf.SmoothStep(0, 1, e / seconds));
                yield return null;
            }
            t.localRotation = toLocal;
        }

        public static IEnumerator Scale(Transform t, Vector3 to, float seconds)
        {
            Vector3 from = t.localScale;
            for (float e = 0; e < seconds; e += Time.deltaTime)
            {
                t.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, e / seconds));
                yield return null;
            }
            t.localScale = to;
        }
    }
}
