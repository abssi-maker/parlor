using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Builds the opera house under the table, in stage space (origin = trapdoor centre on the table plane,
    /// +Y up, +Z away from the seated user). Everything here is below y = 0 and uses portal clip mode 1,
    /// so it is only ever visible through the opening.
    ///
    /// Budget: the whole theatre is 8 meshes / 8 materials (8 draw calls) and about 9k triangles.
    /// The user looks down and forward, so detail is concentrated on the far half (+Z) of the pit.
    /// </summary>
    public static class TheatreBuilder
    {
        public const float RimDepth = 0.040f;       // the table's visible thickness along the cut
        public const float PitDepth = 1.00f;        // stage floor is this far below the table
        public const float PitRadius = 0.44f;
        public const float StageRadius = 0.18f;   // smaller than the opening, so the house around it shows
        public static readonly float[] TierY = { -0.27f, -0.50f, -0.73f };
        const int Seg = 24;

        public sealed class Theatre
        {
            public Transform Root;
            public Transform Lift;          // the circular stage; the magician stands on it. Moves up and down.
            public Transform Audience;      // bounced during applause
            public Transform Placards;      // night 2 reveal; hidden by default
            public Renderer PosterRenderer;
            public Renderer[] GlowRenderers;
        }

        /// <summary>
        /// Leans the whole shaft away from the user: a point at depth d moves d × lean further from them, while every
        /// floor stays level and everything upright stays upright. Seen from a seat, a vertical shaft under a small
        /// opening only ever shows its far wall; leaning it along the line of sight puts the stage, the magician and
        /// the balconies in view, like looking down from the gods of a real opera house. Lean 0 = straight down.
        /// </summary>
        static void Lean(Transform root, float lean)
        {
            if (lean <= 0.001f) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                // Children of the lift are leaned in the lift's own frame (the lift itself slides along the lean).
                Transform frame = root;
                bool skip = false;
                for (Transform t = mf.transform; t && t != root; t = t.parent)
                {
                    if (t.name.StartsWith("NoLean")) skip = true;      // upright set pieces riding on the lift
                    if (t.name == "Lift") { frame = t; break; }
                }
                if (skip) continue;
                Matrix4x4 toFrame = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix, back = toFrame.inverse;
                var mesh = mf.sharedMesh;
                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = toFrame.MultiplyPoint3x4(v[i]);
                    p.z -= lean * p.y;
                    v[i] = back.MultiplyPoint3x4(p);
                }
                mesh.vertices = v;
                mesh.RecalculateBounds();
            }
        }

        public static Theatre Build(Transform stage, float lean = 0f)
        {
            var t = new Theatre();
            var root = new GameObject("Theatre").transform;
            root.SetParent(stage, false);
            t.Root = root;

            BuildShell(root);
            BuildBalconies(root);
            BuildCurtains(root);
            t.Audience = BuildAudience(root, "Audience", 0f).transform;
            t.Placards = BuildPlacards(root).transform;
            t.Placards.gameObject.SetActive(false);
            t.GlowRenderers = BuildLights(root);
            t.PosterRenderer = BuildPosters(root);
            t.Lift = BuildLift(root);
            Lean(root, lean);
            return t;
        }

        // Wall cylinder seen from inside, plus the orchestra floor around the stage.
        static void BuildShell(Transform root)
        {
            var k = new MeshKit();
            float h = PitDepth - RimDepth + 0.08f;
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * PitRadius, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * PitRadius;
                Vector3 top = Vector3.down * RimDepth, bot = Vector3.down * (RimDepth + h);
                float u0 = i * 0.5f, u1 = (i + 1) * 0.5f;
                // inward-facing: seen from the centre, p1 is to the left of p0
                k.Tint(new Color(0.92f, 0.86f, 0.86f)).Quad(p1 + bot, p0 + bot, p0 + top, p1 + top, new Vector2(u1, 0), new Vector2(u0, h * 2.2f));
            }
            k.ToObject("Walls", root, Mats.Lit("wallpaper", "wallpaper", new Color(1.05f, 0.52f, 0.46f), pitGlow: 1f, rim: 0f));

            var f = new MeshKit();
            f.Tint(new Color(0.62f, 0.38f, 0.26f)).At(Vector3.down * (PitDepth + 0.06f)).Disc(PitRadius + 0.01f, Seg);
            f.ToObject("OrchestraFloor", root, Mats.Lit("darkwood", "wood", new Color(0.85f, 0.66f, 0.55f), pitGlow: 1f, rim: 0f, tiling: new Vector2(2, 2)));
        }

        // Three gilded tiers hugging the wall on the far three-quarters of the circle.
        static void BuildBalconies(Transform root)
        {
            var k = new MeshKit();
            float rOut = PitRadius - 0.005f, rIn = PitRadius - 0.075f, panelH = 0.062f;
            foreach (float y in TierY)
            {
                for (int i = 0; i < Seg; i++)
                {
                    if (!OnFarSide(i)) continue;
                    float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    Vector3 up = Vector3.up * y;
                    // floor of the box (seen from above)
                    k.Tint(new Color(0.34f, 0.10f, 0.12f)).Quad(d1 * rIn + up, d0 * rIn + up, d0 * rOut + up, d1 * rOut + up);
                    // gilded front panel, facing the centre; its top edge is the handrail
                    k.Tint(Color.white).Quad(d1 * rIn + up + Vector3.down * 0.012f, d0 * rIn + up + Vector3.down * 0.012f,
                                             d0 * rIn + up + Vector3.up * panelH, d1 * rIn + up + Vector3.up * panelH,
                                             new Vector2(i * 0.5f, 0.02f), new Vector2((i + 1) * 0.5f, 0.98f));
                    // handrail cap
                    k.Tint(Mats.Gold).Quad(d1 * (rIn - 0.006f) + up + Vector3.up * panelH, d0 * (rIn - 0.006f) + up + Vector3.up * panelH,
                                           d0 * (rIn + 0.008f) + up + Vector3.up * panelH, d1 * (rIn + 0.008f) + up + Vector3.up * panelH,
                                           new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f));
                    // soffit under the box
                    k.Tint(Mats.GoldDark).Quad(d0 * rIn + up + Vector3.down * 0.012f, d1 * rIn + up + Vector3.down * 0.012f,
                                               d1 * rOut + up + Vector3.down * 0.012f, d0 * rOut + up + Vector3.down * 0.012f,
                                               new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                }
            }
            k.ToObject("Balconies", root, Mats.Lit("balcony", "balcony", Color.white, pitGlow: 0.85f, rim: 0.5f));
        }

        static bool OnFarSide(int segment)
        {
            // angle 90° is +Z (far from the user). The near side is seen too: the shaft leans, and a user leaning over
            // the opening looks straight down it, where bare wall read as a black hole. Only a narrow gap right under
            // the user's edge of the opening is left out.
            float mid = (segment + 0.5f) * 360f / Seg;
            return Mathf.Abs(Mathf.DeltaAngle(mid, 90f)) < 168f;
        }

        // Tied-back side curtains and a swagged valance with gold fringe, framing the view just under the rim.
        static void BuildCurtains(Transform root)
        {
            var k = new MeshKit();
            float top = -RimDepth - 0.004f;
            float span = 0.62f;
            Color velvet = Color.white, shade = new Color(0.72f, 0.66f, 0.70f);

            // far valance
            k.Tint(velvet).At(new Vector3(0, top, 0.285f)).Drape(span, 0.085f, 7, 0.010f, 3, 0f, 0f, 3f, 0.5f);
            // side valances (rotated to run along Z)
            k.Tint(shade).At(new Vector3(-0.30f, top, 0.06f), Quaternion.Euler(0, 90, 0)).Drape(0.46f, 0.075f, 5, 0.009f, 3, 0f, 0f, 2.4f, 0.5f);
            k.Tint(shade).At(new Vector3(0.30f, top, 0.06f), Quaternion.Euler(0, -90, 0)).Drape(0.46f, 0.075f, 5, 0.009f, 3, 0f, 0f, 2.4f, 0.5f);
            // long tied-back drapes at the far corners, hanging deep into the pit
            k.Tint(velvet).At(new Vector3(-0.235f, top - 0.02f, 0.262f), Quaternion.Euler(0, 28, 0)).Drape(0.20f, 0.74f, 4, 0.016f, 8, 0.55f, -0.06f, 1.2f, 3.4f);
            k.Tint(velvet).At(new Vector3(0.235f, top - 0.02f, 0.262f), Quaternion.Euler(0, -28, 0)).Drape(0.20f, 0.74f, 4, 0.016f, 8, 0.55f, 0.06f, 1.2f, 3.4f);
            // mid drapes along the sides
            k.Tint(shade).At(new Vector3(-0.315f, top - 0.02f, 0.02f), Quaternion.Euler(0, 82, 0)).Drape(0.18f, 0.60f, 3, 0.015f, 7, 0.5f, 0.05f, 1.1f, 2.8f);
            k.Tint(shade).At(new Vector3(0.315f, top - 0.02f, 0.02f), Quaternion.Euler(0, -82, 0)).Drape(0.18f, 0.60f, 3, 0.015f, 7, 0.5f, -0.05f, 1.1f, 2.8f);
            k.ToObject("Curtains", root, Mats.Lit("velvet", "velvet", new Color(1.45f, 1.0f, 1.0f), pitGlow: 0.55f, rim: 0.2f, twoSided: true));

            // gold trim: fringe under each valance, tie-back cords, and the crest
            var g = new MeshKit();
            g.Tint(Mats.Gold);
            Fringe(g, new Vector3(0, top - 0.085f, 0.283f), Quaternion.identity, span, 30);
            Fringe(g, new Vector3(-0.298f, top - 0.075f, 0.06f), Quaternion.Euler(0, 90, 0), 0.46f, 22);
            Fringe(g, new Vector3(0.298f, top - 0.075f, 0.06f), Quaternion.Euler(0, -90, 0), 0.46f, 22);
            // crest: a fleur-de-lis built from three petals and a band, centred on the far valance
            Vector3 crest = new Vector3(0, top - 0.046f, 0.268f);
            g.Tint(Mats.Gold).At(crest, Quaternion.identity, new Vector3(0.011f, 0.030f, 0.006f)).Sphere(1f, 8, 6);
            g.At(crest + new Vector3(-0.015f, -0.004f, 0), Quaternion.Euler(0, 0, 32), new Vector3(0.008f, 0.021f, 0.005f)).Sphere(1f, 8, 6);
            g.At(crest + new Vector3(0.015f, -0.004f, 0), Quaternion.Euler(0, 0, -32), new Vector3(0.008f, 0.021f, 0.005f)).Sphere(1f, 8, 6);
            g.Tint(Mats.GoldDark).At(crest + new Vector3(0, -0.014f, -0.001f), Quaternion.identity, new Vector3(0.024f, 0.005f, 0.006f)).Sphere(1f, 8, 6);
            // tie-backs
            g.Tint(Mats.Gold).At(new Vector3(-0.262f, top - 0.50f, 0.246f), Quaternion.identity, new Vector3(0.022f, 0.008f, 0.022f)).Sphere(1f, 8, 6);
            g.At(new Vector3(0.262f, top - 0.50f, 0.246f), Quaternion.identity, new Vector3(0.022f, 0.008f, 0.022f)).Sphere(1f, 8, 6);
            g.ToObject("GoldTrim", root, Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f));
        }

        static void Fringe(MeshKit g, Vector3 origin, Quaternion rot, float width, int tassels)
        {
            for (int i = 0; i < tassels; i++)
            {
                float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, (i + 0.5f) / tassels);
                float len = 0.016f + 0.004f * Mathf.Abs(Mathf.Sin(i * 1.7f));
                g.At(origin + rot * new Vector3(x, 0, -0.002f), rot * Quaternion.Euler(180, 0, 0)).Cylinder(0.0045f, 0.0018f, len, 5, false, true);
            }
        }

        // Painted 2D spectators standing in the boxes: a 4x2 sprite sheet, one quad each, one draw call for all.
        static GameObject BuildAudience(Transform root, string name, float lift)
        {
            var k = new MeshKit();
            var rng = new System.Random(7);
            float r = PitRadius - 0.045f;
            foreach (float y in TierY)
            {
                for (int i = 0; i < Seg; i++)
                {
                    if (!OnFarSide(i)) continue;
                    float a = (i + 0.5f) * Mathf.PI * 2f / Seg;
                    Vector3 p = new Vector3(Mathf.Cos(a) * r, y + 0.030f + lift, Mathf.Sin(a) * r);
                    int cell = rng.Next(8);
                    Vector2 uv0 = new Vector2((cell % 4) * 0.25f, (cell / 4) * 0.5f);
                    float tone = 0.78f + 0.22f * (float)rng.NextDouble();
                    // face the centre of the pit, tilted up towards the opening so the user sees faces
                    Quaternion rot = Quaternion.LookRotation(new Vector3(p.x, 0, p.z).normalized, Vector3.up) * Quaternion.Euler(-22, 0, 0);
                    k.Tint(new Color(tone, tone, tone)).At(p, rot).Card(0.052f, 0.060f, uv0 + new Vector2(0.01f, 0.01f), uv0 + new Vector2(0.24f, 0.49f));
                }
            }
            return k.ToObject(name, root, Mats.Lit("audience", "audience", Color.white, pitGlow: 0.7f, rim: 0f, cutoff: 0.5f, twoSided: true));
        }

        // Night 2: the audience on the middle tier holds up placards showing the chosen card.
        static GameObject BuildPlacards(Transform root)
        {
            var k = new MeshKit();
            float r = PitRadius - 0.085f;
            float y = TierY[1] + 0.085f;
            for (int i = 0; i < Seg; i++)
            {
                float mid = (i + 0.5f) * 360f / Seg;
                if (Mathf.Abs(Mathf.DeltaAngle(mid, 90f)) > 75f) continue;
                float a = mid * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                Quaternion rot = Quaternion.LookRotation(new Vector3(p.x, 0, p.z).normalized, Vector3.up) * Quaternion.Euler(-38, 0, 0);
                k.Tint(Color.white).At(p, rot).Card(0.050f, 0.070f, Vector2.zero, Vector2.one);
            }
            // the material's texture offset/scale is pointed at the chosen card face at runtime
            return k.ToObject("Placards", root, Mats.Lit("placard", "cards", Color.white, pitGlow: 0.2f, rim: 0f, unlit: 0.35f, twoSided: true));
        }

        // Sconces between the tiers and footlights around the stage: unlit warm bulbs plus additive halos.
        static Renderer[] BuildLights(Transform root)
        {
            var bulbs = new MeshKit();
            var halos = new MeshKit();
            float r = PitRadius - 0.012f;
            for (int tier = 0; tier < TierY.Length; tier++)
            {
                float y = TierY[tier] - 0.075f;
                for (int i = tier % 2; i < Seg; i += 2)
                {
                    if (!OnFarSide(i)) continue;
                    float a = i * Mathf.PI * 2f / Seg;
                    Vector3 p = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    Vector3 inward = -new Vector3(p.x, 0, p.z).normalized;
                    bulbs.Tint(Mats.GoldDark).At(p + inward * 0.004f + Vector3.down * 0.020f).Cylinder(0.003f, 0.005f, 0.020f, 6, false, false);
                    bulbs.Tint(new Color(1f, 0.90f, 0.66f)).At(p + inward * 0.006f, Quaternion.identity, new Vector3(0.011f, 0.014f, 0.011f)).Sphere(1f, 8, 6);
                    // halo faces up and inward so it reads from the opening
                    Quaternion rot = Quaternion.LookRotation((inward + Vector3.up * 1.2f).normalized) * Quaternion.Euler(0, 180, 0);
                    halos.Tint(new Color(1f, 0.74f, 0.40f, 0.55f)).At(p + inward * 0.012f, rot).Card(0.13f, 0.13f, Vector2.zero, Vector2.one, true);
                }
            }
            for (int i = 0; i < 18; i++)
            {
                float a = i * Mathf.PI * 2f / 18;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (StageRadius + 0.035f) + Vector3.down * (PitDepth + 0.045f);
                bulbs.Tint(new Color(1f, 0.92f, 0.70f)).At(p, Quaternion.identity, new Vector3(0.007f, 0.007f, 0.007f)).Sphere(1f, 6, 4);
                halos.Tint(new Color(1f, 0.74f, 0.40f, 0.30f)).At(p + Vector3.up * 0.004f, Quaternion.Euler(-90, 0, 0)).Card(0.06f, 0.06f, Vector2.zero, Vector2.one, true);
            }
            var b = bulbs.ToObject("Bulbs", root, Mats.Lit("bulb", null, Color.white, pitGlow: 0f, rim: 0f, unlit: 1f));
            var h = halos.ToObject("Halos", root, Mats.Fx("halo", Color.white, additive: true, soft: 1f, clip: Mats.ClipBelow));
            return new[] { b.GetComponent<Renderer>(), h.GetComponent<Renderer>() };
        }

        // Tonight's bill, pasted on the far wall between the tiers. Tinted per night.
        static Renderer BuildPosters(Transform root)
        {
            var k = new MeshKit();
            float r = PitRadius - 0.004f;
            foreach (float deg in new[] { 60f, 120f })
            {
                float a = deg * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, TierY[0] + 0.135f, Mathf.Sin(a) * r);
                Quaternion rot = Quaternion.LookRotation(new Vector3(p.x, 0, p.z).normalized, Vector3.up);
                k.Tint(Color.white).At(p, rot).Card(0.085f, 0.12f, Vector2.zero, Vector2.one);
            }
            var go = k.ToObject("Posters", root, Mats.Lit("poster", "poster", Color.white, pitGlow: 0.5f, rim: 0f));
            return go.GetComponent<Renderer>();
        }

        // The circular stage is a lift: a floor disc with gold inlay on a column, so it can carry the magician up.
        static Transform BuildLift(Transform root)
        {
            var lift = new GameObject("Lift").transform;
            lift.SetParent(root, false);
            lift.localPosition = Vector3.down * PitDepth;

            var top = new MeshKit();
            top.Tint(Color.white).Disc(StageRadius, 32);
            top.ToObject("StageFloor", lift, Mats.Lit("stagefloor", "stagefloor", Color.white, pitGlow: 0.6f, rim: 0.15f));

            var col = new MeshKit();
            col.Tint(Mats.Gold).At(Vector3.down * 0.012f).Cylinder(StageRadius + 0.006f, StageRadius + 0.006f, 0.012f, 32, false, false);
            col.Tint(Mats.VelvetDark).At(Vector3.down * 1.3f).Cylinder(StageRadius - 0.02f, StageRadius - 0.004f, 1.288f, 24, false, false);
            for (int i = 1; i <= 4; i++)
                col.Tint(Mats.GoldDark).At(Vector3.down * (0.25f * i)).Cylinder(StageRadius - 0.001f, StageRadius - 0.001f, 0.010f, 24, false, false);
            col.ToObject("LiftColumn", lift, Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f));

            // His own little proscenium: a half-circle of velvet behind him with a gold pelmet and footlights, so that
            // from above he is framed by theatre and not by bare floor. It stays down in the pit (TablePortal folds
            // it away as the lift nears the table).
            var set = new GameObject("NoLean_Backdrop").transform;
            set.SetParent(lift, false);
            var cur = new MeshKit(); var trim = new MeshKit();
            const int panels = 18; const float r = StageRadius - 0.012f, h = 0.20f, a0 = 20f, a1 = 160f;
            for (int i = 0; i < panels; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, (float)i / panels) * Mathf.Deg2Rad, t1 = Mathf.Lerp(a0, a1, (i + 1f) / panels) * Mathf.Deg2Rad;
                // folds: alternate panels sit a little further out
                float r0 = r + (i % 2 == 0 ? 0f : 0.010f), r1 = r + (i % 2 == 0 ? 0.010f : 0f);
                Vector3 p0 = new Vector3(Mathf.Cos(t0) * r0, 0, Mathf.Sin(t0) * r0), p1 = new Vector3(Mathf.Cos(t1) * r1, 0, Mathf.Sin(t1) * r1);
                // faces the centre of the stage (and the user beyond it)
                cur.Tint(i % 2 == 0 ? Color.white : new Color(0.78f, 0.78f, 0.78f)).Quad(p1, p0, p0 + Vector3.up * h, p1 + Vector3.up * h, new Vector2(i * 0.12f, 0), new Vector2((i + 1) * 0.12f, 1f));
                trim.Tint(Mats.Gold).Quad(p1 + Vector3.up * h, p0 + Vector3.up * h, p0 + Vector3.up * (h + 0.016f), p1 + Vector3.up * (h + 0.016f));
            }
            cur.ToObject("Curtain", set, Mats.Lit("velvet", "velvet", Color.white, pitGlow: 0.5f, rim: 0.3f));
            // footlights along the front edge of the stage
            for (int i = 0; i < 7; i++)
            {
                float t = Mathf.Lerp(205f, 335f, i / 6f) * Mathf.Deg2Rad;
                trim.Tint(new Color(1.6f, 1.25f, 0.7f)).At(new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * (StageRadius - 0.02f) + Vector3.up * 0.004f).Sphere(0.007f, 6, 4);
            }
            trim.ToObject("Trim", set, Mats.Lit("gold", null, Color.white, pitGlow: 0.35f, rim: 1.2f));
            return lift;
        }
    }
}
