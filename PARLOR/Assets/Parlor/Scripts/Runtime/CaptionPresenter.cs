using TMPro;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Captions are always on: the magician's voice is wordless, so captions carry every line, and no cue in
    /// the show is audio-only. The caption sits just above the stage (never head-locked, never at the edge of
    /// the field of view) and turns to face the user.
    /// </summary>
    public sealed class CaptionPresenter : MonoBehaviour
    {
        public const float MaxWidth = 0.34f;
        const float LineHeight = 0.0165f;   // about 1.2 degrees at stage distance

        TextMeshPro _text;
        Transform _panel;
        Renderer _panelR;
        MaterialPropertyBlock _mpb;
        float _alpha, _target, _hideAt = -1f;
        IHeadSource _head;
        public string Current { get; private set; } = "";
        public bool Visible => _target > 0.5f;

        public static CaptionPresenter Create(Transform stage, IHeadSource head)
        {
            var go = new GameObject("Captions");
            go.transform.SetParent(stage, false);
            go.transform.localPosition = Layout.Caption;
            var c = go.AddComponent<CaptionPresenter>();
            c._head = head;
            c.Build();
            return c;
        }

        void Build()
        {
            _mpb = new MaterialPropertyBlock();
            var p = new MeshKit();
            p.Card(1f, 1f, Vector2.zero, Vector2.one);
            _panel = p.ToObject("Panel", transform, Mats.Fx("captionpanel", new Color(0.10f, 0.02f, 0.04f, 0.78f), soft: 0.22f, queueOffset: 20)).transform;
            _panel.localPosition = new Vector3(0, 0, 0.002f);
            _panelR = _panel.GetComponent<Renderer>();

            var tgo = new GameObject("Text");
            tgo.transform.SetParent(transform, false);
            // TextMeshPro faces -Z by default, the same way our panel does.
            _text = tgo.AddComponent<TextMeshPro>();
            var font = Fonts.Body;
            if (font) _text.font = font;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.color = Mats.Cream;
            _text.fontSize = 0.20f;                      // world units: TMP font size 1 = 0.1 m line at scale 1
            _text.rectTransform.sizeDelta = new Vector2(MaxWidth, 0.06f);
            _text.sortingOrder = 30;
            _text.text = "";
            var r = _text.GetComponent<Renderer>();
            MeshKit.Quiet(r);
            Apply(0f);
        }

        /// <summary>Shows a line for its duration (or until replaced). Returns how long it will stay.</summary>
        public float Show(Line line)
        {
            Current = line.Text;
            _text.text = line.Text;
            _text.ForceMeshUpdate();
            Vector2 size = _text.GetRenderedValues(false);
            _panel.localScale = new Vector3(Mathf.Min(MaxWidth, size.x) + 0.05f, Mathf.Max(LineHeight, size.y) + 0.030f, 1f);
            _target = 1f;
            _hideAt = Time.time + line.Duration;
            return line.Duration;
        }

        public void Hide() { _target = 0f; _hideAt = -1f; }

        void LateUpdate()
        {
            if (_hideAt > 0f && Time.time >= _hideAt) Hide();
            if (!Mathf.Approximately(_alpha, _target))
            {
                _alpha = Mathf.MoveTowards(_alpha, _target, Time.deltaTime * 5f);
                Apply(_alpha);
            }
            if (_head != null && _alpha > 0f)
            {
                // Face the user's head. Our content faces -Z, so "forward" points away from the user.
                Vector3 away = transform.position - _head.Head.position;
                if (away.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(away.normalized, Vector3.up), 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
        }

        void Apply(float a)
        {
            _text.alpha = a;
            _text.enabled = a > 0.01f;
            _panelR.enabled = a > 0.01f;
            if (!_panelR.enabled) return;
            Color c = _panelR.sharedMaterial.GetColor("_Color"); c.a *= a;
            _panelR.GetPropertyBlock(_mpb); _mpb.SetColor("_Color", c); _panelR.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>Fonts are loaded from Resources/Fonts as TMP font assets created by the editor tooling.</summary>
    public static class Fonts
    {
        static TMP_FontAsset _body, _title, _italic;
        public static TMP_FontAsset Body => _body ? _body : _body = Load("Body");
        public static TMP_FontAsset Title => _title ? _title : _title = Load("Title") ?? Body;
        public static TMP_FontAsset Italic => _italic ? _italic : _italic = Load("Italic") ?? Body;

        static TMP_FontAsset Load(string name)
        {
            var asset = Resources.Load<TMP_FontAsset>("Fonts/" + name);
            if (asset) return asset;
            // Fallback: build a dynamic font asset from a raw font file, so text never renders as missing glyphs.
            var font = Resources.Load<Font>("Fonts/" + name + "Source");
            return font ? TMP_FontAsset.CreateFontAsset(font) : null;
        }
    }
}
