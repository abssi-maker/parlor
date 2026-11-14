using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Parlor
{
    /// <summary>
    /// The whole show uses three shaders and about a dozen materials, all created here.
    /// Shaders and textures are loaded from Resources so they are always in the build
    /// (Shader.Find would silently strip them from an Android player).
    /// </summary>
    public static class Mats
    {
        public const float ClipNone = 0f, ClipBelow = 1f, ClipHole = 2f;

        static Shader _lit, _fx, _hand;
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, Texture2D> Tex = new Dictionary<string, Texture2D>();

        public static Shader LitShader => _lit ? _lit : _lit = Resources.Load<Shader>("Shaders/ParlorLit");
        public static Shader FxShader => _fx ? _fx : _fx = Resources.Load<Shader>("Shaders/ParlorFx");
        public static Shader HandShader => _hand ? _hand : _hand = Resources.Load<Shader>("Shaders/ParlorHandOccluder");

        // The concept-art palette.
        public static readonly Color Velvet = new Color(0.52f, 0.06f, 0.09f);
        public static readonly Color VelvetDark = new Color(0.30f, 0.03f, 0.06f);
        public static readonly Color Gold = new Color(0.93f, 0.71f, 0.28f);
        public static readonly Color GoldDark = new Color(0.62f, 0.42f, 0.14f);
        public static readonly Color Candle = new Color(1.00f, 0.80f, 0.52f);
        public static readonly Color Navy = new Color(0.09f, 0.13f, 0.30f);
        public static readonly Color Burgundy = new Color(0.42f, 0.07f, 0.13f);
        public static readonly Color Skin = new Color(0.93f, 0.72f, 0.58f);
        public static readonly Color Walnut = new Color(0.36f, 0.22f, 0.13f);
        public static readonly Color Cream = new Color(0.96f, 0.93f, 0.85f);
        public static readonly Color Ink = new Color(0.07f, 0.05f, 0.06f);

        /// <summary>Texture from Resources/Art, or null. A missing texture never breaks the show: the tint carries on.</summary>
        public static Texture2D Texture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!Tex.TryGetValue(name, out var t)) { t = Resources.Load<Texture2D>("Art/" + name); Tex[name] = t; }
            return t;
        }

        /// <summary>Opaque material. Cached by key so identical requests share one material (and one batch).</summary>
        public static Material Lit(string key, string texture = null, Color? tint = null, float clip = ClipBelow, float pitGlow = 0f,
                                   float rim = 0.25f, float unlit = 0f, float cutoff = 0f, bool twoSided = false, float clipPad = 0f,
                                   Vector2? tiling = null)
        {
            if (Cache.TryGetValue(key, out var m) && m) return m;
            m = new Material(LitShader) { name = "Parlor_" + key, enableInstancing = true };
            var tex = Texture(texture);
            if (tex) m.SetTexture("_MainTex", tex);
            if (tiling.HasValue) m.SetTextureScale("_MainTex", tiling.Value);
            m.SetColor("_Color", tint ?? Color.white);
            m.SetFloat("_ClipMode", clip);
            m.SetFloat("_ClipPad", clipPad);
            m.SetFloat("_PitGlow", pitGlow);
            m.SetFloat("_Rim", rim);
            m.SetFloat("_Unlit", unlit);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (cutoff > 0f) m.renderQueue = (int)RenderQueue.AlphaTest;
            Cache[key] = m;
            return m;
        }

        /// <summary>Transparent material: alpha blended by default, additive when requested.</summary>
        public static Material Fx(string key, Color tint, bool additive = false, float soft = 0f, string texture = null,
                                  float clip = ClipNone, int queueOffset = 0, bool alwaysOnTop = false)
        {
            if (Cache.TryGetValue(key, out var m) && m) return m;
            m = new Material(FxShader) { name = "Parlor_" + key, enableInstancing = true };
            var tex = Texture(texture);
            if (tex) m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", tint);
            m.SetFloat("_Soft", soft);
            m.SetFloat("_ClipMode", clip);
            if (additive)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.One);
                m.SetFloat("_SrcAlpha", (float)BlendMode.Zero); m.SetFloat("_DstAlpha", (float)BlendMode.One);
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcAlpha", (float)BlendMode.One); m.SetFloat("_DstAlpha", (float)BlendMode.OneMinusSrcAlpha);
            }
            m.SetFloat("_ZTest", alwaysOnTop ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
            Cache[key] = m;
            return m;
        }

        public static Material Hand(Color ghost)
        {
            const string key = "hand";
            if (!Cache.TryGetValue(key, out var m) || !m)
            {
                m = new Material(HandShader) { name = "Parlor_hand" };
                Cache[key] = m;
            }
            m.SetColor("_Ghost", ghost);
            return m;
        }

        public static void ClearCache()
        {
            foreach (var m in Cache.Values) if (m) Object.Destroy(m);
            Cache.Clear();
        }
    }
}
