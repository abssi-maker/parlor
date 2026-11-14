using System.Collections;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// The trapdoor in the table. Lives on a child of the stage root. Its local space is "portal space":
    /// origin at the centre of the opening on the table plane, +Y up, +Z away from the seated user.
    /// While it is closed the portal can be moved anywhere on the table (the hero trick opens it under
    /// the coin; night 3 opens it where the user taps), and the theatre underneath moves with it.
    ///
    /// The opening is not a mesh. It is a rectangle published to the shaders (see ParlorPortal.hlsl);
    /// everything below the table plane is clipped to what can be seen through it. This component owns
    /// that rectangle, the wood rim that gives the table its thickness, the two sliding door leaves,
    /// the warm light spilling onto the real table, and the theatre underneath.
    /// </summary>
    public sealed class TablePortal : MonoBehaviour
    {
        public static readonly Vector2 DefaultHalfSize = new Vector2(0.15f, 0.11f);
        public const float MinHalf = 0.07f;

        public Vector2 FullHalfSize { get; private set; } = DefaultHalfSize;
        /// <summary>0 = closed, 1 = fully open. The opening grows from a slit along the centre line.</summary>
        public float Open01 { get; private set; }
        /// <summary>Finale: the opening widens past its normal size (1 = normal).</summary>
        public float Widen { get; private set; } = 1f;
        public bool IsOpen => Open01 > 0.001f;
        public Vector2 CurrentHalf => new Vector2(FullHalfSize.x * Widen * Open01, FullHalfSize.y * Widen);
        public float MaxWiden { get; private set; } = 1.35f;

        public TheatreBuilder.Theatre Theatre { get; private set; }
        public Transform Lift => Theatre.Lift;
        /// <summary>Lift height in stage space: 0 = flush with the table top, -PitDepth = down on the stage.</summary>
        public float LiftY { get => Theatre.Lift.localPosition.y; set => Theatre.Lift.localPosition = new Vector3(0, value, 0); }

        Transform _rim, _leafL, _leafR, _table;
        Renderer _spill, _spot;
        MaterialPropertyBlock _mpb;
        float _spotLevel, _spotTarget;

        static readonly int IdWorldToStage = Shader.PropertyToID("_ParlorWorldToStage");
        static readonly int IdHole = Shader.PropertyToID("_ParlorHole");
        static readonly int IdPit = Shader.PropertyToID("_ParlorPit");
        static readonly int IdKey = Shader.PropertyToID("_ParlorKeyDir");
        static readonly int IdWarm = Shader.PropertyToID("_ParlorWarm");
        static readonly int IdColor = Shader.PropertyToID("_Color");

        /// <summary>Safe shader globals before any portal exists (and in tests).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetGlobals()
        {
            Shader.SetGlobalMatrix(IdWorldToStage, Matrix4x4.identity);
            Shader.SetGlobalVector(IdHole, Vector4.zero);
            Shader.SetGlobalVector(IdPit, new Vector4(-TheatreBuilder.RimDepth, -TheatreBuilder.PitDepth, 1, 0));
            Shader.SetGlobalVector(IdKey, new Vector4(-0.3f, 0.8f, -0.5f, 0).normalized);
            Shader.SetGlobalVector(IdWarm, Mats.Candle);
        }

        /// <summary>
        /// Builds the portal. maxHalf is how much table there is around the stage origin (from the table's
        /// PlaneRect); the opening shrinks to fit small tables and the finale widening is limited to match.
        /// </summary>
        public void Build(Vector2 maxHalf, bool virtualTable)
        {
            _mpb = new MaterialPropertyBlock();
            float fit = Mathf.Min(1f, Mathf.Min(maxHalf.x / (DefaultHalfSize.x + 0.03f), maxHalf.y / (DefaultHalfSize.y + 0.03f)));
            fit = Mathf.Max(fit, MinHalf / DefaultHalfSize.y);
            FullHalfSize = DefaultHalfSize * fit;
            MaxWiden = Mathf.Clamp(Mathf.Min(maxHalf.x / FullHalfSize.x, maxHalf.y / FullHalfSize.y) * 0.92f, 1f, 1.35f);

            Theatre = TheatreBuilder.Build(transform);
            BuildRim();
            BuildLeaves();
            BuildLight();
            if (virtualTable) BuildVirtualTable();
            Apply();
        }

        void BuildRim()
        {
            var k = new MeshKit();
            float d = TheatreBuilder.RimDepth, bevel = 0.003f;
            Color lip = new Color(1.25f, 1.12f, 1.0f), body = new Color(0.62f, 0.52f, 0.46f), deep = new Color(0.30f, 0.24f, 0.22f);
            // Unit frame at x = ±1, z = ±1, scaled to the opening. Each inner face: bevel highlight, body, dark foot.
            void Face(Vector3 bl, Vector3 br, float uLen)
            {
                Vector3 up = Vector3.up;
                k.Tint(lip).Quad(bl - up * bevel, br - up * bevel, br, bl, new Vector2(0, 0.90f), new Vector2(uLen, 1f));
                k.Tint(body).Quad(bl - up * (d * 0.8f), br - up * (d * 0.8f), br - up * bevel, bl - up * bevel, new Vector2(0, 0.30f), new Vector2(uLen, 0.90f));
                k.Tint(deep).Quad(bl - up * d, br - up * d, br - up * (d * 0.8f), bl - up * (d * 0.8f), new Vector2(0, 0.22f), new Vector2(uLen, 0.30f));
            }
            Face(new Vector3(-1, 0, 1), new Vector3(1, 0, 1), 1.3f);     // far side, faces the user
            Face(new Vector3(1, 0, 1), new Vector3(1, 0, -1), 1.0f);     // right side, faces the centre
            Face(new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), 1.0f);   // left side
            Face(new Vector3(1, 0, -1), new Vector3(-1, 0, -1), 1.3f);   // near side (seen when leaning over)
            _rim = k.ToObject("Rim", transform, Mats.Lit("rim", "wood", Color.white, clip: Mats.ClipBelow, clipPad: 0.0015f, rim: 0.1f, tiling: new Vector2(1f, 0.35f))).transform;
        }

        void BuildLeaves()
        {
            var mat = Mats.Lit("leaf", "wood", new Color(0.80f, 0.72f, 0.66f), clip: Mats.ClipBelow, rim: 0.1f);
            _leafL = BuildLeaf("LeafL", -1f, mat);
            _leafR = BuildLeaf("LeafR", 1f, mat);
        }

        // A leaf is a slab just under the table surface. Local origin at its inner edge; it extends outwards (sign * x).
        Transform BuildLeaf(string name, float sign, Material mat)
        {
            var k = new MeshKit();
            float top = -0.004f, thick = 0.014f;
            Vector3 a = new Vector3(0, top, -1), b = new Vector3(sign, top, -1), c = new Vector3(sign, top, 1), d = new Vector3(0, top, 1);
            if (sign > 0) k.Tint(Color.white).Quad(a, b, c, d, Vector2.zero, new Vector2(1f, 1.2f));
            else k.Tint(Color.white).Quad(b, a, d, c, Vector2.zero, new Vector2(1f, 1.2f));
            // inner edge, facing the slit
            Vector3 e0 = new Vector3(0, top - thick, -1), e1 = new Vector3(0, top - thick, 1);
            if (sign > 0) k.Tint(new Color(0.5f, 0.42f, 0.38f)).Quad(e1, e0, a, d, new Vector2(0, 0.1f), new Vector2(1.2f, 0.2f));
            else k.Tint(new Color(0.5f, 0.42f, 0.38f)).Quad(e0, e1, d, a, new Vector2(0, 0.1f), new Vector2(1.2f, 0.2f));
            // brass edge strip
            k.Tint(Mats.Gold * 1.1f).Quad(new Vector3(0, top + 0.0004f, -1), new Vector3(sign * 0.035f, top + 0.0004f, -1),
                                          new Vector3(sign * 0.035f, top + 0.0004f, 1), new Vector3(0, top + 0.0004f, 1), new Vector2(0.4f, 0.4f), new Vector2(0.45f, 0.45f));
            return k.ToObject(name, transform, mat).transform;
        }

        void BuildLight()
        {
            // warm light spilling out of the opening onto the table around it (cut away over the opening itself)
            var s = new MeshKit();
            s.At(Vector3.up * 0.0008f).Ground(1f, 1f);
            _spill = s.ToObject("Spill", transform, Mats.Fx("spill", new Color(1f, 0.66f, 0.30f, 0.0f), additive: true, soft: 0.85f, clip: Mats.ClipHole)).GetComponent<Renderer>();

            // the spotlight that "finds" the table at the start of the show
            var p = new MeshKit();
            p.At(Vector3.up * 0.0006f).Ground(0.62f, 0.62f);
            _spot = p.ToObject("Spot", transform, Mats.Fx("spot", new Color(1f, 0.86f, 0.62f, 0.0f), additive: true, soft: 0.7f, clip: Mats.ClipHole, queueOffset: -1)).GetComponent<Renderer>();
        }

        // Tier 3: no real table, so the trapdoor opens in a small virtual parlour table instead.
        void BuildVirtualTable()
        {
            float w = 0.92f, dpt = 0.64f, th = 0.03f, legH = 0.70f;
            var top = new MeshKit();
            top.Tint(Color.white).Ground(w, dpt, 2f);
            var root = new GameObject("VirtualTable").transform;
            // The table belongs to the stage, not to the opening: the opening can move across it.
            root.SetParent(transform.parent ? transform.parent : transform, false);
            top.ToObject("Top", root, Mats.Lit("tabletop", "wood", new Color(0.92f, 0.84f, 0.78f), clip: Mats.ClipHole, rim: 0.12f));

            var body = new MeshKit();
            // apron: the slab's four edges
            body.Tint(new Color(0.62f, 0.52f, 0.46f));
            Vector3 h = new Vector3(w * 0.5f, 0, dpt * 0.5f);
            body.Quad(new Vector3(-h.x, -th, -h.z), new Vector3(h.x, -th, -h.z), new Vector3(h.x, 0, -h.z), new Vector3(-h.x, 0, -h.z), Vector2.zero, new Vector2(2, 0.1f));
            body.Quad(new Vector3(h.x, -th, h.z), new Vector3(-h.x, -th, h.z), new Vector3(-h.x, 0, h.z), new Vector3(h.x, 0, h.z), Vector2.zero, new Vector2(2, 0.1f));
            body.Quad(new Vector3(-h.x, -th, h.z), new Vector3(-h.x, -th, -h.z), new Vector3(-h.x, 0, -h.z), new Vector3(-h.x, 0, h.z), Vector2.zero, new Vector2(1.4f, 0.1f));
            body.Quad(new Vector3(h.x, -th, -h.z), new Vector3(h.x, -th, h.z), new Vector3(h.x, 0, h.z), new Vector3(h.x, 0, -h.z), Vector2.zero, new Vector2(1.4f, 0.1f));
            // legs sit outside the pit radius so they can never be seen through the opening in front of the theatre
            foreach (var s in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                body.Tint(new Color(0.45f, 0.36f, 0.32f)).At(new Vector3(s.x * (h.x - 0.035f), -th - legH, s.y * (h.z - 0.035f))).Cylinder(0.014f, 0.024f, legH, 8, true, false, 3f);
            body.ToObject("Body", root, Mats.Lit("tablebody", "wood", new Color(0.92f, 0.84f, 0.78f), clip: Mats.ClipNone, rim: 0.12f));
            _table = root;
        }

        public void SetOpen(float open01) { Open01 = Mathf.Clamp01(open01); Apply(); }
        public void SetWiden(float widen) { Widen = Mathf.Clamp(widen, 1f, MaxWiden); Apply(); }
        public void SetSpot(float level) { _spotTarget = Mathf.Clamp01(level); }

        public IEnumerator AnimateOpen(float to, float seconds)
        {
            float from = Open01;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                float x = t / seconds;
                // ease out with a small overshoot settle: the leaves "clack" against their stops
                float e = 1f - Mathf.Pow(1f - x, 3f);
                SetOpen(Mathf.LerpUnclamped(from, to, e));
                yield return null;
            }
            SetOpen(to);
        }

        public IEnumerator AnimateWiden(float to, float seconds)
        {
            float from = Widen; to = Mathf.Clamp(to, 1f, MaxWiden);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                SetWiden(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)));
                yield return null;
            }
            SetWiden(to);
        }

        public IEnumerator AnimateLift(float toY, float seconds)
        {
            float from = LiftY;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                LiftY = Mathf.Lerp(from, toY, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
            LiftY = toY;
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(_spotLevel, _spotTarget))
            {
                _spotLevel = Mathf.MoveTowards(_spotLevel, _spotTarget, Time.deltaTime * 1.2f);
                Apply();
            }
            else PublishGlobals();   // the stage can move (anchor updates, recenter)
        }

        void Apply()
        {
            if (Theatre == null) return;
            Vector2 half = CurrentHalf;
            Vector2 full = FullHalfSize * Widen;

            _rim.localScale = new Vector3(full.x, 1f, full.y);
            bool moving = Open01 > 0.001f && Open01 < 0.999f;
            _leafL.gameObject.SetActive(moving);
            _leafR.gameObject.SetActive(moving);
            _leafL.localPosition = new Vector3(-half.x, 0, 0);
            _leafR.localPosition = new Vector3(half.x, 0, 0);
            _leafL.localScale = _leafR.localScale = new Vector3(full.x, 1f, full.y);

            // Nothing under the table costs anything while the trapdoor is shut.
            Theatre.Root.gameObject.SetActive(IsOpen);
            _rim.gameObject.SetActive(IsOpen);

            _spill.transform.localScale = new Vector3(full.x * 2f + 0.34f, 1f, full.y * 2f + 0.34f);
            SetAlpha(_spill, 0.30f * Open01);
            SetAlpha(_spot, 0.42f * _spotLevel);
            PublishGlobals();
        }

        void SetAlpha(Renderer r, float a)
        {
            r.enabled = a > 0.004f;
            if (!r.enabled) return;
            Color c = r.sharedMaterial.GetColor(IdColor); c.a = a;
            r.GetPropertyBlock(_mpb); _mpb.SetColor(IdColor, c); r.SetPropertyBlock(_mpb);
        }

        void PublishGlobals()
        {
            Vector2 half = CurrentHalf;
            Shader.SetGlobalMatrix(IdWorldToStage, transform.worldToLocalMatrix);
            Shader.SetGlobalVector(IdHole, new Vector4(half.x, half.y, 0, 0));
            Shader.SetGlobalVector(IdPit, new Vector4(-TheatreBuilder.RimDepth, -TheatreBuilder.PitDepth, 1, 0));
            // key light comes from above and from the user's side, so faces turned to the user are lit
            Shader.SetGlobalVector(IdKey, transform.TransformDirection(new Vector3(-0.30f, 0.80f, -0.52f).normalized));
            Shader.SetGlobalVector(IdWarm, Mats.Candle);
        }

        void OnDisable() { Shader.SetGlobalVector(IdHole, Vector4.zero); }
    }
}
