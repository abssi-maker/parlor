using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// The magician: an original stylised character, 18 cm tall, built in code from primitives
    /// (top hat, curled moustache, navy tailcoat with gold trim, burgundy waistcoat, white gloves).
    /// One rigidly skinned mesh, one material, one draw call. Animation is procedural: each gesture
    /// sets target rotations on a few bones and a critically damped blend carries them there, so
    /// gestures can interrupt each other at any time without popping.
    /// Model space: feet at the origin, facing +Z.
    /// </summary>
    public sealed class MagicianActor : MonoBehaviour
    {
        public const float Scale = 1.35f;
        public const float Height = 0.18f * Scale;

        enum B { Root, Hips, Chest, Head, Hat, Moustache, Lids, ArmR, ForeR, HandR, ArmL, ForeL, HandL, LegR, LegL, Wand, Pupils, BrowL, BrowR, Mouth, Cape, Count }

        /// <summary>What his face is doing: drives the brows (and nothing else), so every gesture reads on his face.</summary>
        public enum Mood { Neutral, Delight, Worried, Angry, Sly }

        readonly Transform[] _bones = new Transform[(int)B.Count];
        readonly Quaternion[] _rest = new Quaternion[(int)B.Count];
        readonly Vector3[] _target = new Vector3[(int)B.Count];     // euler offsets from rest
        readonly Quaternion[] _current = new Quaternion[(int)B.Count];
        Vector3 _hipsRest;
        float _hop, _hopVel, _talk, _blinkAt, _blink, _squash = 1f;
        float _yaw, _yawTarget;
        bool _wandOut;
        float _wandScale;
        Coroutine _gesture;
        SkinnedMeshRenderer _smr;
        // the Mishap: 0 = himself, 1 = possessed (bigger, darker, red eyes, horns). Eased, never snapped on screen.
        float _monster, _monsterTarget, _hatAskew, _hatAskewTarget, _shake;
        Renderer _eyes;
        Transform _horns;
        MaterialPropertyBlock _mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color");
        public bool IsMonster => _monsterTarget > 0.5f;

        public Transform RightHand => _bones[(int)B.HandR];
        public Transform LeftHand => _bones[(int)B.HandL];
        public Transform HeadBone => _bones[(int)B.Head];
        public Transform WandTip { get; private set; }
        /// <summary>World-space point to look at (the user's head). Null = look straight ahead.</summary>
        public Vector3? LookTarget;
        public bool Talking { set { _talkOn = value; } }
        bool _talkOn;
        /// <summary>Where his eyes go (the user's moving hand, the coin). Null = wherever his head is looking.</summary>
        public Vector3? EyeTarget;
        /// <summary>Loudness of the line he is speaking right now, 0..1, or a negative value when unknown (then his
        /// mouth flaps on a rhythm instead). Wired to AudioDirector.VoiceLevel.</summary>
        public System.Func<float> VoiceLevel;
        public string Name => "The Great Thimble";
        Mood _mood;
        float _browL, _browR, _browLift, _saccadeAt;
        Vector2 _saccade, _pupil;
        Vector3 _pupilRest, _browLRest, _browRRest, _mouthRest;
        float _capeSwing, _capeVel, _lastYaw, _mouthOpen;
        public void SetMood(Mood m) { _mood = m; }
        public float Stiffness = 14f;

        public static MagicianActor Create(Transform parent)
        {
            var go = new GameObject("Magician");
            go.transform.SetParent(parent, false);
            // Built at 18 cm, shown at 24 cm: large enough to read his face from a seat, still small enough to stand
            // inside the trapdoor opening.
            go.transform.localScale = Vector3.one * Scale;
            var m = go.AddComponent<MagicianActor>();
            m.Build();
            return m;
        }

        // ---------------------------------------------------------------- construction

        [System.Serializable] class PartData { public string bone; public float[] v, n, c; public int[] t; }

        /// <summary>A sculpted part from Resources/Art/thimble (made by tools in ArtGen/thimble), or false if absent.</summary>
        static bool Part(MeshKit k, string name, B bone)
        {
            var ta = Resources.Load<TextAsset>("Art/thimble/" + name);
            if (!ta) return false;
            var d = JsonUtility.FromJson<PartData>(ta.text);
            if (d == null || d.v == null || d.t == null || d.n == null || d.c == null) return false;
            Matrix4x4 keep = k.Xf; k.Xf = Matrix4x4.identity;
            k.Skin((int)bone).Raw(d.v, d.n, d.c, d.t);
            k.Xf = keep;
            return true;
        }

        Transform Bone(B b, B parent, Vector3 modelPos)
        {
            var t = new GameObject(b.ToString()).transform;
            t.SetParent(b == B.Root ? transform : _bones[(int)parent], false);
            t.position = transform.TransformPoint(modelPos);
            t.rotation = transform.rotation;
            _bones[(int)b] = t;
            return t;
        }

        void Build()
        {
            Bone(B.Root, B.Root, Vector3.zero);
            Bone(B.Hips, B.Root, new Vector3(0, 0.058f, 0));
            Bone(B.Chest, B.Hips, new Vector3(0, 0.084f, 0));
            Bone(B.Head, B.Chest, new Vector3(0, 0.112f, 0));
            Bone(B.Hat, B.Head, new Vector3(0, 0.150f, 0));
            Bone(B.Moustache, B.Head, new Vector3(0, 0.126f, 0.021f));
            Bone(B.Lids, B.Head, new Vector3(0, 0.1415f, 0.0f));
            Bone(B.ArmR, B.Chest, new Vector3(0.0265f, 0.106f, 0));
            Bone(B.ForeR, B.ArmR, new Vector3(0.0265f, 0.079f, 0));
            Bone(B.HandR, B.ForeR, new Vector3(0.0265f, 0.052f, 0));
            Bone(B.ArmL, B.Chest, new Vector3(-0.0265f, 0.106f, 0));
            Bone(B.ForeL, B.ArmL, new Vector3(-0.0265f, 0.079f, 0));
            Bone(B.HandL, B.ForeL, new Vector3(-0.0265f, 0.052f, 0));
            Bone(B.LegR, B.Hips, new Vector3(0.0105f, 0.058f, 0));
            Bone(B.LegL, B.Hips, new Vector3(-0.0105f, 0.058f, 0));
            Bone(B.Wand, B.HandR, new Vector3(0.0265f, 0.046f, 0.004f));
            Bone(B.Pupils, B.Head, new Vector3(0, 0.1395f, 0.0215f));
            Bone(B.BrowL, B.Head, new Vector3(-0.0092f, 0.1478f, 0.0196f));
            Bone(B.BrowR, B.Head, new Vector3(0.0092f, 0.1478f, 0.0196f));
            Bone(B.Mouth, B.Head, new Vector3(0, 0.1225f, 0.0195f));
            Bone(B.Cape, B.Chest, new Vector3(0, 0.110f, -0.0215f));

            var k = new MeshKit();
            Color navy = Mats.Navy, gold = Mats.Gold, white = new Color(0.98f, 0.97f, 0.95f), black = Mats.Ink;
            Color skin = Mats.Skin, hair = new Color(0.16f, 0.09f, 0.06f), burgundy = Mats.Burgundy;

            // legs and shoes (sculpted shoes with spats when available)
            bool shoes = Part(k, "shoe_r", B.LegR) & Part(k, "shoe_l", B.LegL);
            foreach (var (bone, x) in new[] { (B.LegR, 0.0105f), (B.LegL, -0.0105f) })
            {
                k.Skin((int)bone).Tint(new Color(0.11f, 0.11f, 0.16f)).At(new Vector3(x, 0.006f, 0)).Capsule(0.0085f, 0.056f, 8);
                if (shoes) continue;
                k.Tint(black).At(new Vector3(x, 0.0055f, 0.006f), Quaternion.identity, new Vector3(0.0095f, 0.0058f, 0.016f)).Sphere(1f, 8, 6);
                k.Tint(white).At(new Vector3(x, 0.0075f, 0.004f), Quaternion.identity, new Vector3(0.0078f, 0.0040f, 0.009f)).Sphere(1f, 6, 4);   // spats
            }

            // hips and coat tails
            k.Skin((int)B.Hips).Tint(navy).At(new Vector3(0, 0.060f, 0), Quaternion.identity, new Vector3(0.0215f, 0.014f, 0.017f)).Sphere(1f, 10, 6);
            foreach (float x in new[] { -0.009f, 0.009f })
                k.Tint(navy * 0.9f).At(new Vector3(x, 0.066f, -0.013f), Quaternion.Euler(-12, 0, x > 0 ? -7 : 7), new Vector3(0.0085f, 0.036f, 0.0035f))
                 .At2(new Vector3(0, -0.5f, 0)).Sphere(1f, 6, 6);
            // gold piping along the tails
            foreach (float x in new[] { -0.0155f, 0.0155f })
                k.Tint(gold).At(new Vector3(x, 0.040f, -0.019f), Quaternion.Euler(-12, 0, 0), new Vector3(0.0012f, 0.018f, 0.0012f)).Sphere(1f, 4, 4);

            // torso: tailcoat, waistcoat, shirt, tie, buttons
            k.Skin((int)B.Chest).Tint(navy).At(new Vector3(0, 0.086f, 0), Quaternion.identity, new Vector3(0.0235f, 0.0265f, 0.0185f)).Sphere(1f, 12, 8);
            k.Tint(burgundy).At(new Vector3(0, 0.082f, 0.0075f), Quaternion.identity, new Vector3(0.0145f, 0.0215f, 0.0135f)).Sphere(1f, 10, 8);
            k.Tint(white).At(new Vector3(0, 0.099f, 0.0115f), Quaternion.identity, new Vector3(0.0075f, 0.0095f, 0.0085f)).Sphere(1f, 8, 6);
            k.Tint(new Color(0.75f, 0.08f, 0.10f)).At(new Vector3(0, 0.0965f, 0.0185f), Quaternion.identity, new Vector3(0.0028f, 0.0085f, 0.0022f)).Sphere(1f, 6, 4);
            k.Tint(new Color(0.75f, 0.08f, 0.10f)).At(new Vector3(0, 0.1045f, 0.0185f), Quaternion.identity, new Vector3(0.0036f, 0.0026f, 0.0024f)).Sphere(1f, 6, 4);
            for (int i = 0; i < 3; i++)
                k.Tint(gold).At(new Vector3(0, 0.086f - i * 0.0075f, 0.0208f - i * 0.0006f), Quaternion.identity, Vector3.one * 0.0017f).Sphere(1f, 5, 4);
            // lapels with gold edge
            foreach (float s in new[] { -1f, 1f })
            {
                k.Tint(navy * 1.15f).At(new Vector3(s * 0.0125f, 0.096f, 0.0125f), Quaternion.Euler(0, s * 28, s * -16), new Vector3(0.0058f, 0.0165f, 0.0030f)).Sphere(1f, 6, 6);
                k.Tint(gold).At(new Vector3(s * 0.0090f, 0.095f, 0.0150f), Quaternion.Euler(0, s * 28, s * -16), new Vector3(0.0011f, 0.0160f, 0.0011f)).Sphere(1f, 4, 4);
                // epaulette
                k.Tint(gold).At(new Vector3(s * 0.0235f, 0.1085f, 0), Quaternion.identity, new Vector3(0.0075f, 0.0030f, 0.0075f)).Sphere(1f, 8, 4);
            }
            // collar
            k.Tint(white).At(new Vector3(0, 0.110f, 0.001f)).Cylinder(0.0105f, 0.0085f, 0.006f, 10, false, false);

            // head: the sculpted head (skull, face, nose, ears, hair) when available, else primitives
            bool sculpted = Part(k, "head", B.Head);
            k.Skin((int)B.Head);
            Vector3 hc = new Vector3(0, 0.135f, 0);
            if (!sculpted)
            {
                k.Tint(skin).At(hc, Quaternion.identity, new Vector3(0.0235f, 0.0225f, 0.0225f)).Sphere(1f, 14, 10);
                k.Tint(skin).At(hc + new Vector3(0, -0.012f, 0.004f), Quaternion.identity, new Vector3(0.0165f, 0.0125f, 0.017f)).Sphere(1f, 10, 8);       // jaw
                k.Tint(skin * 0.96f).At(hc + new Vector3(0, -0.002f, 0.0235f), Quaternion.identity, new Vector3(0.0042f, 0.0045f, 0.0050f)).Sphere(1f, 8, 6); // nose
            }
            foreach (float s in new[] { -1f, 1f })
            {
                if (!sculpted) k.Tint(skin * 0.95f).At(hc + new Vector3(s * 0.0232f, 0, 0), Quaternion.identity, new Vector3(0.0035f, 0.0058f, 0.0045f)).Sphere(1f, 6, 6);    // ear
                k.Tint(white).At(hc + new Vector3(s * 0.0090f, 0.0048f, 0.0188f), Quaternion.identity, new Vector3(0.0056f, 0.0064f, 0.0040f)).Sphere(1f, 8, 6);  // eye
                k.Skin((int)B.Pupils);
                k.Tint(new Color(0.20f, 0.12f, 0.08f)).At(hc + new Vector3(s * 0.0086f, 0.0044f, 0.0220f), Quaternion.identity, new Vector3(0.0028f, 0.0034f, 0.0018f)).Sphere(1f, 6, 6);
                k.Tint(white).At(hc + new Vector3(s * 0.0078f, 0.0058f, 0.0236f), Quaternion.identity, Vector3.one * 0.0009f).Sphere(1f, 4, 4);                 // eye glint
                k.Skin((int)(s > 0 ? B.BrowR : B.BrowL));
                k.Tint(hair).At(hc + new Vector3(s * 0.0092f, 0.0128f, 0.0196f), Quaternion.Euler(0, 0, s * -12), new Vector3(0.0068f, 0.0017f, 0.0022f)).Sphere(1f, 6, 4);  // brow
                k.Skin((int)B.Head);
                if (sculpted) continue;
                k.Tint(hair).At(hc + new Vector3(s * 0.0205f, -0.004f, 0.006f), Quaternion.identity, new Vector3(0.0040f, 0.0105f, 0.0080f)).Sphere(1f, 6, 6);   // sideburn
                k.Tint(new Color(0.96f, 0.62f, 0.55f)).At(hc + new Vector3(s * 0.0145f, -0.0055f, 0.0165f), Quaternion.identity, new Vector3(0.0048f, 0.0034f, 0.0030f)).Sphere(1f, 6, 4); // cheek
            }
            if (!sculpted) k.Tint(hair).At(hc + new Vector3(0, 0.004f, -0.006f), Quaternion.identity, new Vector3(0.0238f, 0.0205f, 0.0205f)).Sphere(1f, 10, 8);     // hair at the back
            // smile: on the Mouth bone, which opens with his voice
            k.Skin((int)B.Mouth);
            k.Tint(new Color(0.45f, 0.10f, 0.10f)).At(hc + new Vector3(0, -0.0125f, 0.0190f), Quaternion.identity, new Vector3(0.0068f, 0.0022f, 0.0030f)).Sphere(1f, 8, 4);
            k.Tint(white).At(hc + new Vector3(0, -0.0115f, 0.0200f), Quaternion.identity, new Vector3(0.0050f, 0.0011f, 0.0020f)).Sphere(1f, 6, 4);

            // cape: a short burgundy evening cape from the shoulders, flaring at the hem; it swings when he turns or hops
            // (behind the coat: the chest's back is at z = -0.0185, the folds are ±0.0022 deep)
            k.Skin((int)B.Cape).Tint(burgundy * 0.85f).At(new Vector3(0, 0.110f, -0.0215f), Quaternion.Euler(8, 0, 0)).Drape(0.050f, 0.086f, 3, 0.0022f, 5, -0.45f, 0f);

            // moustache: two tapering curls
            k.Skin((int)B.Moustache).Tint(hair);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 mo = new Vector3(0, 0.1265f, 0.0222f);
                k.At(mo + new Vector3(s * 0.0048f, 0, 0), Quaternion.Euler(0, 0, s * 10), new Vector3(0.0058f, 0.0024f, 0.0026f)).Sphere(1f, 8, 6);
                k.At(mo + new Vector3(s * 0.0108f, 0.0012f, -0.0012f), Quaternion.Euler(0, 0, s * 32), new Vector3(0.0046f, 0.0016f, 0.0020f)).Sphere(1f, 6, 4);
                k.At(mo + new Vector3(s * 0.0142f, 0.0042f, -0.0024f), Quaternion.Euler(0, 0, s * 70), new Vector3(0.0030f, 0.0011f, 0.0014f)).Sphere(1f, 6, 4);
            }

            // eyelids: scaled open/closed by the Lids bone
            k.Skin((int)B.Lids).Tint(skin * 0.93f);
            foreach (float s in new[] { -1f, 1f })
                k.At(new Vector3(s * 0.0090f, 0.1415f, 0.0190f), Quaternion.identity, new Vector3(0.0062f, 0.0068f, 0.0046f)).At2(new Vector3(0, -0.5f, 0)).Sphere(1f, 8, 6);

            // top hat (sculpted when available)
            if (!Part(k, "hat", B.Hat))
            {
            k.Skin((int)B.Hat);
            k.Tint(black).At(new Vector3(0, 0.1505f, -0.001f), Quaternion.Euler(-4, 0, 3)).Cylinder(0.0300f, 0.0300f, 0.0026f, 16);
            k.Tint(new Color(0.10f, 0.08f, 0.10f)).At(new Vector3(0, 0.1525f, -0.001f), Quaternion.Euler(-4, 0, 3)).Cylinder(0.0185f, 0.0208f, 0.0330f, 16);
            k.Tint(new Color(0.62f, 0.08f, 0.12f)).At(new Vector3(0, 0.1530f, -0.001f), Quaternion.Euler(-4, 0, 3)).Cylinder(0.0190f, 0.0192f, 0.0068f, 16, false, false);
            k.Tint(gold).At(new Vector3(0.0005f, 0.1562f, 0.0183f), Quaternion.identity, new Vector3(0.0024f, 0.0034f, 0.0010f)).Sphere(1f, 5, 4);
            }

            // arms, cuffs, gloves (sculpted gloves with fingers when available)
            bool gloves = Part(k, "hand_r", B.HandR) & Part(k, "hand_l", B.HandL);
            foreach (var (arm, fore, hand, x) in new[] { (B.ArmR, B.ForeR, B.HandR, 0.0265f), (B.ArmL, B.ForeL, B.HandL, -0.0265f) })
            {
                k.Skin((int)arm).Tint(navy).At(new Vector3(x, 0.077f, 0)).Capsule(0.0074f, 0.033f, 8);
                k.Skin((int)fore).Tint(navy).At(new Vector3(x, 0.052f, 0)).Capsule(0.0066f, 0.030f, 8);
                k.Tint(gold).At(new Vector3(x, 0.0545f, 0)).Cylinder(0.0074f, 0.0074f, 0.0032f, 8, false, false);
                k.Tint(white).At(new Vector3(x, 0.0520f, 0)).Cylinder(0.0070f, 0.0070f, 0.0030f, 8, false, false);
                if (gloves) continue;
                k.Skin((int)hand).Tint(white).At(new Vector3(x, 0.0455f, 0), Quaternion.identity, new Vector3(0.0082f, 0.0092f, 0.0070f)).Sphere(1f, 8, 6);
                k.At(new Vector3(x, 0.0470f, 0.0062f), Quaternion.identity, new Vector3(0.0030f, 0.0044f, 0.0030f)).Sphere(1f, 5, 4);   // thumb
            }

            // wand (scaled in and out by the Wand bone)
            k.Skin((int)B.Wand);
            k.Tint(black).At(new Vector3(0.0265f, 0.046f, 0.004f), Quaternion.Euler(90, 0, 0)).Cylinder(0.0016f, 0.0016f, 0.044f, 6);
            k.Tint(white).At(new Vector3(0.0265f, 0.046f, 0.042f), Quaternion.Euler(90, 0, 0)).Cylinder(0.0018f, 0.0018f, 0.007f, 6);
            WandTip = new GameObject("WandTip").transform;
            WandTip.SetParent(_bones[(int)B.Wand], false);
            WandTip.position = transform.TransformPoint(new Vector3(0.0265f, 0.046f, 0.050f));

            var mesh = k.ToMesh("Magician");
            var bind = new Matrix4x4[(int)B.Count];
            for (int i = 0; i < bind.Length; i++) bind[i] = _bones[i].worldToLocalMatrix * transform.localToWorldMatrix;
            mesh.bindposes = bind;

            _smr = gameObject.AddComponent<SkinnedMeshRenderer>();
            _smr.sharedMesh = mesh;
            _smr.bones = _bones;
            _smr.rootBone = _bones[(int)B.Root];
            _smr.sharedMaterial = Mats.Lit("character", null, Color.white, clip: Mats.ClipBelow, rim: 0.75f);
            _smr.localBounds = new Bounds(new Vector3(0, 0.09f, 0), new Vector3(0.22f, 0.26f, 0.22f));
            _smr.updateWhenOffscreen = false;
            MeshKit.Quiet(_smr);

            // Monster form (the Mishap): glowing eyes and two little horns, hidden until he is possessed.
            var eyes = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                eyes.Tint(new Color(1.6f, 0.15f, 0.08f)).At(hc + new Vector3(s * 0.0090f, 0.0048f, 0.0206f), Quaternion.identity, new Vector3(0.0060f, 0.0052f, 0.0030f)).Sphere(1f, 8, 6);
            var eyeGo = eyes.ToObject("MonsterEyes", transform, Mats.Lit("monstereyes", null, Color.white, clip: Mats.ClipBelow, rim: 0f, unlit: 1f));
            eyeGo.transform.SetParent(_bones[(int)B.Head], true);
            _eyes = eyeGo.GetComponent<Renderer>();
            var horns = new MeshKit();
            foreach (float s in new[] { -1f, 1f })
                horns.Tint(new Color(0.45f, 0.06f, 0.10f)).At(new Vector3(s * 0.016f, 0.172f, 0.002f), Quaternion.Euler(0, 0, s * -28)).Cylinder(0.0055f, 0.0005f, 0.026f, 8, true, false);
            var hornGo = horns.ToObject("MonsterHorns", transform, Mats.Lit("monsterhorns", null, Color.white, clip: Mats.ClipBelow, rim: 1.0f));
            hornGo.transform.SetParent(_bones[(int)B.Hat], true);
            _horns = hornGo.transform;
            _eyes.enabled = false; _horns.gameObject.SetActive(false);
            _mpb = new MaterialPropertyBlock();

            for (int i = 0; i < (int)B.Count; i++) { _rest[i] = _bones[i].localRotation; _current[i] = _rest[i]; }
            _hipsRest = _bones[(int)B.Hips].localPosition;
            _bones[(int)B.Wand].localScale = Vector3.zero;
            _bones[(int)B.Lids].localScale = new Vector3(1, 0.02f, 1);
            _pupilRest = _bones[(int)B.Pupils].localPosition;
            _browLRest = _bones[(int)B.BrowL].localPosition;
            _browRRest = _bones[(int)B.BrowR].localPosition;
            _mouthRest = _bones[(int)B.Mouth].localPosition;
            _blinkAt = 1.5f;
            Rest();
        }

        // ---------------------------------------------------------------- pose plumbing

        void Set(B b, float x, float y, float z) => _target[(int)b] = new Vector3(x, y, z);

        /// <summary>Relaxed standing pose: arms slightly out and bent, a showman at ease.</summary>
        void Rest()
        {
            for (int i = 0; i < _target.Length; i++) _target[i] = Vector3.zero;
            _mood = Mood.Neutral;
            Set(B.ArmR, 6, 0, 14); Set(B.ForeR, -22, 0, 0);
            Set(B.ArmL, 6, 0, -14); Set(B.ForeL, -22, 0, 0);
        }

        void Update()
        {
            if (!_bones[0] || !_bones[(int)B.Count - 1]) return;     // torn down (re-placement, leaving Play mode)
            float dt = Time.deltaTime;
            float tNow = Time.time;
            float k = 1f - Mathf.Exp(-Stiffness * dt);

            // breathing, weight shift, and talking bob layered on top of the gesture targets
            float breathe = Mathf.Sin(tNow * 1.9f);
            _talk = Mathf.MoveTowards(_talk, _talkOn ? 1f : 0f, dt * 6f);
            float lv = VoiceLevel != null ? VoiceLevel() : -1f;
            float openTo = lv >= 0f ? lv : _talk * (0.5f + 0.5f * Mathf.Sin(tNow * 17f));
            _mouthOpen = Mathf.Lerp(_mouthOpen, openTo, 1f - Mathf.Exp(-30f * dt));
            // with a known voice level, the head nods on the accents of the line rather than on a fixed beat
            float chatter = lv >= 0f ? (_mouthOpen - 0.35f) * 1.6f : _talk * Mathf.Sin(tNow * 17f);

            for (int i = 0; i < (int)B.Count; i++)
            {
                Vector3 e = _target[i];
                if (i == (int)B.Chest) e += new Vector3(breathe * 1.2f, 0, Mathf.Sin(tNow * 0.9f) * 1.5f);
                if (i == (int)B.Head) e += new Vector3(chatter * 3.0f + _lookPitch, _lookYaw, Mathf.Sin(tNow * 1.3f) * 2f + chatter * 1.5f);
                if (i == (int)B.Moustache) e += new Vector3(-_mouthOpen * 16f, 0, Mathf.Sin(tNow * 2.3f) * 2f);
                if (i == (int)B.Cape) e += new Vector3(_capeSwing + breathe * 1.5f, 0, 0);
                if (i == (int)B.Hat) e += new Vector3(chatter * -2f, 0, _hatAskew);
                if (i == (int)B.Head && _shake > 0f) e += new Vector3(0, Mathf.Sin(tNow * 38f) * 14f * _shake, 0);
                if (i == (int)B.ArmR || i == (int)B.ArmL) e += new Vector3(breathe * 1.5f, 0, 0);
                _current[i] = Quaternion.Slerp(_current[i], _rest[i] * Quaternion.Euler(e), k);
                _bones[i].localRotation = _current[i];
            }

            // hop (celebrate) and squash/stretch
            _hopVel -= 9.0f * dt;
            _hop = Mathf.Max(0f, _hop + _hopVel * dt);
            if (_hop <= 0f) _hopVel = 0f;
            _squash = Mathf.Lerp(_squash, 1f + Mathf.Clamp(_hopVel * 0.25f, -0.12f, 0.16f), 1f - Mathf.Exp(-20f * dt));
            _bones[(int)B.Hips].localPosition = _hipsRest + new Vector3(0, _hop + breathe * 0.0006f, 0);
            _bones[(int)B.Root].localScale = new Vector3(1f / Mathf.Sqrt(_squash), _squash, 1f / Mathf.Sqrt(_squash));

            // blink
            _blinkAt -= dt;
            if (_blinkAt <= 0f) { _blink = 0.13f; _blinkAt = Random.Range(2.2f, 5.0f); }
            _blink = Mathf.Max(0f, _blink - dt);
            float lid = _blink > 0f ? 1f : 0.02f;
            var lids = _bones[(int)B.Lids];
            lids.localScale = new Vector3(1, Mathf.Lerp(lids.localScale.y, lid, 1f - Mathf.Exp(-45f * dt)), 1);

            // wand
            _wandScale = Mathf.MoveTowards(_wandScale, _wandOut ? 1f : 0f, dt * 7f);
            _bones[(int)B.Wand].localScale = Vector3.one * _wandScale;

            UpdateMonster(dt, tNow);
            UpdateLook(dt);
            UpdateFace(dt, tNow);
        }

        // Brows from the mood, pupils on the eye target with small saccades, mouth open with the voice, cape swing.
        void UpdateFace(float dt, float tNow)
        {
            Mood m = _monster > 0.5f ? Mood.Angry : _mood;
            // (degrees of roll for the left and right brow, lift in model metres); inner ends up = worried
            float l = 0f, r = 0f, lift = 0f;
            switch (m)
            {
                case Mood.Delight: l = -6f; r = 6f; lift = 0.0019f; break;
                case Mood.Worried: l = 20f; r = -20f; lift = 0.0010f; break;
                case Mood.Angry: l = -22f; r = 22f; lift = -0.0007f; break;
                case Mood.Sly: l = -4f; r = -14f; lift = 0.0004f; break;
            }
            float kb = 1f - Mathf.Exp(-12f * dt);
            _browL = Mathf.Lerp(_browL, l, kb); _browR = Mathf.Lerp(_browR, r, kb); _browLift = Mathf.Lerp(_browLift, lift, kb);
            float slyR = m == Mood.Sly ? 0.0012f : 0f;
            _bones[(int)B.BrowL].localPosition = _browLRest + Vector3.up * _browLift;
            _bones[(int)B.BrowR].localPosition = _browRRest + Vector3.up * (_browLift + slyR);
            _bones[(int)B.BrowL].localRotation = _rest[(int)B.BrowL] * Quaternion.Euler(0, 0, _browL);
            _bones[(int)B.BrowR].localRotation = _rest[(int)B.BrowR] * Quaternion.Euler(0, 0, _browR);

            // eyes: towards the target, inside the whites; a tiny darting saccade every second or two keeps them alive
            _saccadeAt -= dt;
            if (_saccadeAt <= 0f) { _saccade = Random.insideUnitCircle * 0.00025f; _saccadeAt = Random.Range(0.6f, 1.8f); }
            Vector2 want = _saccade;
            Vector3? target = EyeTarget ?? LookTarget;
            var pupils = _bones[(int)B.Pupils];
            if (target.HasValue)
            {
                Vector3 d = HeadBone.InverseTransformDirection(target.Value - pupils.position);
                if (d.z > 0.01f) want += new Vector2(Mathf.Clamp(d.x / d.z, -1f, 1f) * 0.0013f, Mathf.Clamp(d.y / d.z, -1f, 1f) * 0.0010f);
            }
            _pupil = Vector2.Lerp(_pupil, want, 1f - Mathf.Exp(-25f * dt));
            pupils.localPosition = _pupilRest + new Vector3(_pupil.x, _pupil.y, 0);

            var mouth = _bones[(int)B.Mouth];
            mouth.localScale = new Vector3(1f - 0.15f * _mouthOpen, 1f + 2.2f * _mouthOpen, 1f);
            mouth.localPosition = _mouthRest + Vector3.down * (0.0009f * _mouthOpen);

            // cape: a damped spring kicked by turning and hopping
            float yawVel = Mathf.DeltaAngle(_lastYaw, _yaw) / Mathf.Max(dt, 1e-4f); _lastYaw = _yaw;
            float force = -_capeSwing * 60f - _capeVel * 9f + Mathf.Abs(yawVel) * 0.35f + Mathf.Max(0f, -_hopVel) * 40f + _hopVel * -15f;
            _capeVel += force * dt;
            _capeSwing = Mathf.Clamp(_capeSwing + _capeVel * dt, -12f, 35f);
        }

        void UpdateMonster(float dt, float tNow)
        {
            _monster = Mathf.MoveTowards(_monster, _monsterTarget, dt * 0.9f);
            _hatAskew = Mathf.Lerp(_hatAskew, _hatAskewTarget, 1f - Mathf.Exp(-10f * dt));
            float m = Mathf.SmoothStep(0f, 1f, _monster);
            // he swells to half as big again, with a slow menacing breath
            float swell = 1f + 0.55f * m + 0.03f * m * Mathf.Sin(tNow * 2.4f);
            transform.localScale = Vector3.one * Scale * swell;
            if (_smr)
            {
                _smr.GetPropertyBlock(_mpb);
                _mpb.SetColor(IdColor, Color.Lerp(Color.white, new Color(0.46f, 0.26f, 0.55f), m));
                _smr.SetPropertyBlock(_mpb);
            }
            bool on = m > 0.35f && (_smr == null || _smr.enabled);
            if (_eyes)
            {
                _eyes.enabled = on;
                // the eyes pulse slowly (well under three flashes a second)
                _eyes.transform.localScale = Vector3.one * (0.9f + 0.18f * Mathf.Sin(tNow * 4f));
            }
            if (_horns) { _horns.gameObject.SetActive(on); _horns.localScale = Vector3.one * Mathf.Clamp01((m - 0.35f) / 0.65f); }
        }

        /// <summary>Possessed (true) or himself again (false). snap = no transition (canonical beat starts).</summary>
        public void SetMonster(bool on, bool snap = false)
        {
            _monsterTarget = on ? 1f : 0f;
            if (snap) { _monster = _monsterTarget; UpdateMonster(0f, Time.time); }
        }

        /// <summary>Hat knocked crooked after the scuffle (degrees of roll; 0 = straight).</summary>
        public void SetHatAskew(float degrees) { _hatAskewTarget = degrees; }

        /// <summary>Head shake: 0 = still, 1 = violent "fighting it from inside".</summary>
        public void SetShake(float amount) { _shake = Mathf.Clamp01(amount); }

        float _lookYaw, _lookPitch;

        // Turn to face the user: the body yaws most of the way, the head finishes and tilts up.
        void UpdateLook(float dt)
        {
            float yawTo = 0f, pitchTo = 0f, bodyTo = _yawTarget;
            if (LookTarget.HasValue)
            {
                Vector3 local = transform.parent ? transform.parent.InverseTransformPoint(LookTarget.Value) : LookTarget.Value;
                local -= transform.localPosition + Vector3.up * 0.135f;
                float worldYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                bodyTo = worldYaw;
                float flat = new Vector2(local.x, local.z).magnitude;
                pitchTo = -Mathf.Clamp(Mathf.Atan2(local.y, Mathf.Max(flat, 0.01f)) * Mathf.Rad2Deg, -20f, 50f) * 0.75f;
                yawTo = Mathf.Clamp(Mathf.DeltaAngle(_yaw, worldYaw), -40f, 40f);
            }
            _yaw = Mathf.LerpAngle(_yaw, bodyTo, 1f - Mathf.Exp(-3.5f * dt));
            _lookYaw = Mathf.Lerp(_lookYaw, yawTo, 1f - Mathf.Exp(-8f * dt));
            _lookPitch = Mathf.Lerp(_lookPitch, pitchTo, 1f - Mathf.Exp(-8f * dt));
            transform.localRotation = Quaternion.Euler(0, _yaw, 0);
        }

        /// <summary>Body heading in the parent's space when there is no look target (180 = facing the user).</summary>
        public void Face(float yawDegrees, bool snap = false)
        {
            _yawTarget = yawDegrees;
            if (snap) { _yaw = yawDegrees; transform.localRotation = Quaternion.Euler(0, _yaw, 0); }
        }

        // He spends part of the show under a closed trapdoor, where the whole theatre is switched off. A gesture
        // asked for while he is inactive starts the moment he is switched back on.
        IEnumerator _pending;

        void Play(IEnumerator routine)
        {
            if (!isActiveAndEnabled) { _gesture = null; _pending = routine; return; }
            if (_gesture != null) StopCoroutine(_gesture);
            _pending = null;
            _gesture = StartCoroutine(routine);
        }

        void OnEnable()
        {
            if (_pending == null) return;
            var r = _pending; _pending = null;
            _gesture = StartCoroutine(r);
        }

        void OnDisable() { _gesture = null; }   // Unity stops coroutines when the object is deactivated

        static WaitForSeconds Wait(float s) => new WaitForSeconds(s);

        // ---------------------------------------------------------------- gestures

        public void Idle() { Play(IdleRoutine()); }
        // Idle is never a statue: every few seconds, when he is not speaking, a small habit of his.
        IEnumerator IdleRoutine()
        {
            Rest(); _wandOut = false;
            int n = Random.Range(0, 4);
            while (true)
            {
                yield return Wait(Random.Range(4.5f, 7.5f));
                if (_talkOn) continue;
                switch (n++ % 4)
                {
                    case 0:   // straightens his hat by the brim
                        Set(B.ArmR, -150, 0, 34); Set(B.ForeR, -95, 0, 0); Set(B.Head, 4, 0, -6);
                        yield return Wait(0.35f);
                        Set(B.Hat, 0, 0, -9); yield return Wait(0.25f);
                        Set(B.Hat, 0, 0, 3); yield return Wait(0.25f);
                        Set(B.Hat, 0, 0, 0); break;
                    case 1:   // taps his foot, glancing at the user: "any time you're ready"
                        _mood = Mood.Sly;
                        for (int i = 0; i < 4; i++) { Set(B.LegR, -18, 0, 0); yield return Wait(0.16f); Set(B.LegR, 0, 0, 0); yield return Wait(0.16f); }
                        _mood = Mood.Neutral; break;
                    case 2:   // dusts off a shoulder, very pleased with himself
                        Set(B.ArmL, -95, 30, -10); Set(B.ForeL, -120, 0, 0); Set(B.Head, 0, 22, 0);
                        for (int i = 0; i < 2; i++) { Set(B.ForeL, -132, 0, 0); yield return Wait(0.14f); Set(B.ForeL, -112, 0, 0); yield return Wait(0.14f); }
                        _mood = Mood.Delight; break;
                    default:  // twirls the end of his moustache
                        Set(B.ArmL, -110, 0, -14); Set(B.ForeL, -118, 0, 0); _mood = Mood.Sly;
                        for (int i = 0; i < 3; i++) { Set(B.HandL, 0, 0, 25); yield return Wait(0.18f); Set(B.HandL, 0, 0, -10); yield return Wait(0.18f); }
                        break;
                }
                yield return Wait(0.5f);
                Rest();
            }
        }

        /// <summary>Cowering beside something big: crouched, leaning on it, peeking out the other way. Holds.</summary>
        public void Cower(float side) { Play(CowerRoutine(side)); }
        IEnumerator CowerRoutine(float side)
        {
            Rest(); _mood = Mood.Worried;
            Set(B.Hips, 14, 0, side * 8); Set(B.LegR, -22, 0, 0); Set(B.LegL, -22, 0, 0);
            Set(B.Chest, 8, 0, side * 14);
            Set(B.ArmR, -105, 0, 24); Set(B.ForeR, -115, 0, 0);
            Set(B.ArmL, -105, 0, -24); Set(B.ForeL, -115, 0, 0);
            for (int i = 0; ; i++)
            {
                // trembling, and peeking out now and then
                Set(B.Head, -4, (i % 6 < 2 ? -side * 32f : -side * 6f), i % 2 == 0 ? 3 : -3);
                yield return Wait(0.12f);
            }
        }

        /// <summary>A deep theatrical bow, hat arm sweeping out.</summary>
        public float Bow()
        {
            Play(BowRoutine());
            return 1.9f;
        }
        IEnumerator BowRoutine()
        {
            Rest(); _mood = Mood.Delight;
            Set(B.ArmR, -20, 0, 70); Set(B.ForeR, -30, 0, 0);
            Set(B.ArmL, 30, 0, -20); Set(B.ForeL, -80, 0, 0);
            yield return Wait(0.25f);
            Set(B.Hips, 38, 0, 0); Set(B.Chest, 30, 0, 0); Set(B.Head, 18, 0, 0);
            Set(B.LegR, -38, 0, 0); Set(B.LegL, -38, 0, 0);
            yield return Wait(0.95f);
            Rest();
        }

        /// <summary>A big friendly wave.</summary>
        public float Wave()
        {
            Play(WaveRoutine());
            return 1.6f;
        }
        IEnumerator WaveRoutine()
        {
            Rest(); _mood = Mood.Delight;
            Set(B.ArmR, 0, 0, 150);
            for (int i = 0; i < 4; i++)
            {
                Set(B.ForeR, 0, 0, i % 2 == 0 ? 40 : -15);
                Set(B.Chest, 0, 0, i % 2 == 0 ? -5 : 4);
                yield return Wait(0.26f);
            }
            Rest();
        }

        /// <summary>"Show me your hands": both palms raised and offered, repeated.</summary>
        public float ShowHands()
        {
            Play(ShowHandsRoutine());
            return 1.8f;
        }
        IEnumerator ShowHandsRoutine()
        {
            Rest(); _mood = Mood.Sly;
            for (int i = 0; i < 2; i++)
            {
                Set(B.ArmR, -70, 0, 28); Set(B.ForeR, -35, 0, 0);
                Set(B.ArmL, -70, 0, -28); Set(B.ForeL, -35, 0, 0);
                Set(B.Head, -8, 0, 0);
                yield return Wait(0.55f);
                Set(B.ArmR, -45, 0, 22); Set(B.ArmL, -45, 0, -22);
                yield return Wait(0.3f);
            }
            Rest();
        }

        /// <summary>Wand out, a flourish, then a tap straight down. Returns the time of the tap.</summary>
        public float WandTap()
        {
            Play(WandTapRoutine());
            return 0.95f;
        }
        IEnumerator WandTapRoutine()
        {
            Rest(); _wandOut = true;
            Set(B.ArmR, -150, 0, 20); Set(B.ForeR, -20, 0, 0); Set(B.Chest, -8, 10, 0);
            yield return Wait(0.6f);
            Set(B.ArmR, -55, 0, 8); Set(B.ForeR, -8, 0, 0); Set(B.Chest, 14, -6, 0);
            yield return Wait(0.35f);
            _hopVel = 0.25f;
            yield return Wait(0.7f);
            Rest(); _wandOut = false;
        }

        /// <summary>Reach up with the right hand to catch something falling from above. Holds until released.</summary>
        public void ReachUp()
        {
            Play(ReachRoutine());
        }
        IEnumerator ReachRoutine()
        {
            Rest();
            Set(B.ArmR, -160, 0, 18); Set(B.ForeR, -12, 0, 0);
            Set(B.ArmL, 10, 0, -40); Set(B.ForeL, -30, 0, 0);
            Set(B.Head, -24, 0, 0); Set(B.Chest, -8, 0, 0);
            yield break;
        }

        /// <summary>Present whatever is in the right hand, palm up and forward.</summary>
        public void Present()
        {
            Play(PresentRoutine());
        }
        IEnumerator PresentRoutine()
        {
            Rest(); _mood = Mood.Delight;
            Set(B.ArmR, -78, 0, 16); Set(B.ForeR, -28, 0, 0);
            Set(B.ArmL, 14, 0, -34); Set(B.ForeL, -60, 0, 0);
            Set(B.Chest, -4, -8, 0);
            yield break;
        }

        /// <summary>Point with the left hand in a direction given in the parent's space.</summary>
        public float Point(Vector3 localDirection)
        {
            Play(PointRoutine(localDirection));
            return 1.2f;
        }
        IEnumerator PointRoutine(Vector3 dir)
        {
            Rest();
            Vector3 d = Quaternion.Inverse(transform.localRotation) * dir.normalized;
            float pitch = -90f - Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) * Mathf.Rad2Deg;
            float side = Mathf.Clamp(Mathf.Atan2(d.x, Mathf.Max(0.05f, d.z)) * Mathf.Rad2Deg, -70, 70);
            Set(B.ArmL, pitch, side, -8); Set(B.ForeL, -4, 0, 0);
            Set(B.Chest, 0, side * 0.25f, 0);
            yield return Wait(1.1f);
            Rest();
        }

        /// <summary>Arms thrown up with two happy hops.</summary>
        public float Celebrate()
        {
            Play(CelebrateRoutine());
            return 1.5f;
        }
        IEnumerator CelebrateRoutine()
        {
            Rest(); _mood = Mood.Delight;
            Set(B.ArmR, 0, 0, 155); Set(B.ArmL, 0, 0, -155);
            Set(B.ForeR, 0, 0, 15); Set(B.ForeL, 0, 0, -15);
            Set(B.Head, -14, 0, 0);
            for (int i = 0; i < 2; i++) { _hopVel = 0.42f; yield return Wait(0.45f); }
            yield return Wait(0.4f);
            Rest();
        }

        /// <summary>The Lesson demo: hold out a hand, close it, open it. Returns total duration.</summary>
        public float DemonstrateVanish()
        {
            Play(VanishDemoRoutine());
            return 2.6f;
        }
        IEnumerator VanishDemoRoutine()
        {
            Rest(); _mood = Mood.Sly;
            Set(B.ArmR, -82, 0, 10); Set(B.ForeR, -20, 0, 0); Set(B.Head, 10, -14, 0);
            yield return Wait(0.7f);
            HandClosed(true);
            Set(B.ForeR, -42, 0, 0);
            yield return Wait(0.8f);
            HandClosed(false);
            Set(B.ForeR, -14, 0, 0); Set(B.Head, -6, 0, 0);
            yield return Wait(0.9f);
            Rest();
        }

        /// <summary>Palm-down flat hand lowered and lifted: the "cover the case" demo.</summary>
        public float DemonstrateCover()
        {
            Play(CoverDemoRoutine());
            return 2.0f;
        }
        IEnumerator CoverDemoRoutine()
        {
            Rest();
            for (int i = 0; i < 2; i++)
            {
                Set(B.ArmR, -60, 0, 10); Set(B.ForeR, -50, 0, 0); Set(B.HandR, 60, 0, 0);
                yield return Wait(0.55f);
                Set(B.ArmR, -85, 0, 10); Set(B.ForeR, -20, 0, 0);
                yield return Wait(0.4f);
            }
            Rest();
        }

        /// <summary>A puzzled chin-stroke.</summary>
        public float Ponder()
        {
            Play(PonderRoutine());
            return 1.5f;
        }
        IEnumerator PonderRoutine()
        {
            Rest(); _mood = Mood.Sly;
            Set(B.ArmL, -40, 0, -6); Set(B.ForeL, -125, 0, 0);
            Set(B.Head, 6, 0, 10); Set(B.Chest, 0, 8, 0);
            yield return Wait(1.4f);
            Rest();
        }

        /// <summary>Knocking upwards on the underside of the table (the return-night entrance).</summary>
        public float KnockUp()
        {
            Play(KnockRoutine());
            return 1.3f;
        }
        IEnumerator KnockRoutine()
        {
            Rest(); _mood = Mood.Delight;
            Set(B.Head, -30, 0, 0);
            for (int i = 0; i < 3; i++)
            {
                Set(B.ArmR, -170, 0, 6); Set(B.ForeR, -4, 0, 0);
                yield return Wait(0.16f);
                Set(B.ArmR, -140, 0, 6); Set(B.ForeR, -40, 0, 0);
                yield return Wait(0.22f);
            }
            Rest();
        }

        /// <summary>Comic panic: arms flailing over his head, little hops, for the given time.</summary>
        public float Panic(float seconds = 1.8f)
        {
            Play(PanicRoutine(seconds));
            return seconds;
        }
        IEnumerator PanicRoutine(float seconds)
        {
            Rest(); _mood = Mood.Worried;
            for (float t = 0; t < seconds; t += 0.18f)
            {
                bool a = Mathf.RoundToInt(t / 0.18f) % 2 == 0;
                Set(B.ArmR, a ? -150 : -120, 0, a ? 40 : 70); Set(B.ForeR, a ? -40 : -10, 0, 0);
                Set(B.ArmL, a ? -120 : -150, 0, a ? -70 : -40); Set(B.ForeL, a ? -10 : -40, 0, 0);
                Set(B.Head, -10, a ? 12 : -12, 0);
                if (a && Mathf.RoundToInt(t / 0.18f) % 4 == 0) _hopVel = 0.12f;   // small hops: he stays on the table
                yield return Wait(0.18f);
            }
            Rest();
        }

        /// <summary>Possessed: hunched forward, clawed arms raised wide. Holds until another gesture.</summary>
        public void Menace() { Play(MenaceRoutine()); }
        IEnumerator MenaceRoutine()
        {
            Rest(); _wandOut = false;
            Set(B.Chest, 22, 0, 0); Set(B.Head, -18, 0, 0); Set(B.Hips, 8, 0, 0);
            while (true)
            {
                Set(B.ArmR, -135, 0, 55); Set(B.ForeR, -55, 0, 0); Set(B.HandR, 40, 0, 0);
                Set(B.ArmL, -135, 0, -55); Set(B.ForeL, -55, 0, 0); Set(B.HandL, 40, 0, 0);
                yield return Wait(0.9f);
                Set(B.ArmR, -115, 0, 70); Set(B.ForeR, -70, 0, 0);
                Set(B.ArmL, -115, 0, -70); Set(B.ForeL, -70, 0, 0);
                yield return Wait(0.9f);
            }
        }

        /// <summary>Fighting it from inside: hands clutching his head, which shakes.</summary>
        public void Struggle() { Play(StruggleRoutine()); }
        IEnumerator StruggleRoutine()
        {
            Rest(); _mood = Mood.Worried;
            Set(B.ArmR, -150, 0, 30); Set(B.ForeR, -110, 0, 0);
            Set(B.ArmL, -150, 0, -30); Set(B.ForeL, -110, 0, 0);
            Set(B.Chest, 12, 0, 0);
            yield break;
        }

        /// <summary>Sheepish: a hand rubs the back of his neck, a small embarrassed bow.</summary>
        public float Sheepish()
        {
            Play(SheepishRoutine());
            return 2.0f;
        }
        IEnumerator SheepishRoutine()
        {
            Rest(); _mood = Mood.Worried;
            Set(B.ArmR, -150, 0, 20); Set(B.ForeR, -120, 0, 0); Set(B.Head, 12, 0, -10);
            for (int i = 0; i < 3; i++) { Set(B.ForeR, i % 2 == 0 ? -125 : -105, 0, 0); yield return Wait(0.25f); }
            Set(B.Hips, 14, 0, 0); Set(B.Chest, 10, 0, 0);
            yield return Wait(0.8f);
            Rest();
        }

        /// <summary>Grabbed from below: arms up and reaching, legs kicking.</summary>
        public void Dragged() { Play(DraggedRoutine()); }
        IEnumerator DraggedRoutine()
        {
            Rest(); _mood = Mood.Worried;
            for (int i = 0; ; i++)
            {
                Set(B.ArmR, -170, 0, i % 2 == 0 ? 10 : 30); Set(B.ArmL, -170, 0, i % 2 == 0 ? -30 : -10);
                Set(B.LegR, i % 2 == 0 ? -30 : 10, 0, 0); Set(B.LegL, i % 2 == 0 ? 10 : -30, 0, 0);
                Set(B.Head, -30, 0, 0);
                yield return Wait(0.14f);
            }
        }

        void HandClosed(bool closed)
        {
            RightHand.localScale = closed ? new Vector3(0.82f, 0.78f, 0.95f) : Vector3.one;
        }

        public void SetVisible(bool on) { if (_smr) _smr.enabled = on; }
    }

    static class MeshKitPivot
    {
        /// <summary>Shifts the next shape inside the current transform (in the shape's own unit space), so a scaled
        /// sphere can hang from its top instead of its centre.</summary>
        public static MeshKit At2(this MeshKit k, Vector3 unitOffset) { k.Xf *= Matrix4x4.Translate(unitOffset); return k; }
    }
}
