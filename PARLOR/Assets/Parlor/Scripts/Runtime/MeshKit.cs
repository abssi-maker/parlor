using System.Collections.Generic;
using UnityEngine;

namespace Parlor
{
    /// <summary>
    /// Small procedural mesh builder. PARLOR's whole set is generated in code: it keeps the build tiny,
    /// lets everything share a handful of materials (vertex colours carry the palette), and means the
    /// theatre can be rebuilt to fit whatever table the user has.
    /// Many shapes are appended into one MeshKit, so a whole prop or a whole theatre tier is one draw call.
    /// </summary>
    public sealed class MeshKit
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color32> C = new List<Color32>();
        public readonly List<int> T = new List<int>();
        public readonly List<BoneWeight> W = new List<BoneWeight>();   // only filled when Bone >= 0

        public Matrix4x4 Xf = Matrix4x4.identity;   // applied to everything appended
        public Color32 Col = Color.white;
        public int Bone = -1;                        // >= 0: rigid skinning to that bone index

        public int Triangles => T.Count / 3;

        public MeshKit At(Vector3 pos) { Xf = Matrix4x4.Translate(pos); return this; }
        public MeshKit At(Vector3 pos, Quaternion rot) { Xf = Matrix4x4.TRS(pos, rot, Vector3.one); return this; }
        public MeshKit At(Vector3 pos, Quaternion rot, Vector3 scale) { Xf = Matrix4x4.TRS(pos, rot, scale); return this; }
        public MeshKit Tint(Color c) { Col = c; return this; }
        public MeshKit Skin(int bone) { Bone = bone; return this; }

        int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(Xf.MultiplyPoint3x4(p));
            N.Add(Xf.MultiplyVector(n).normalized);
            UV.Add(uv); C.Add(Col);
            if (Bone >= 0) W.Add(new BoneWeight { boneIndex0 = Bone, weight0 = 1f });
            return V.Count - 1;
        }

        void Tri(int a, int b, int c) { T.Add(a); T.Add(b); T.Add(c); }

        /// <summary>Appends a ready-made mesh part (flat xyz / rgb arrays, triangle indices), rigid on the current bone.</summary>
        public MeshKit Raw(float[] v, float[] n, float[] c, int[] t)
        {
            int start = V.Count, count = v.Length / 3;
            for (int i = 0; i < count; i++)
            {
                Col = new Color(c[i * 3], c[i * 3 + 1], c[i * 3 + 2]);
                Add(new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]), new Vector3(n[i * 3], n[i * 3 + 1], n[i * 3 + 2]), Vector2.zero);
            }
            // Orient every triangle by its own vertex normals, whatever winding convention the generator used: Unity
            // draws a triangle's front where Cross(b - a, c - a) points (an earlier fixed flip showed the parts' insides).
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int ia = start + t[i], ib = start + t[i + 1], ic = start + t[i + 2];
                Vector3 face = Vector3.Cross(V[ib] - V[ia], V[ic] - V[ia]);
                if (Vector3.Dot(face, N[ia] + N[ib] + N[ic]) >= 0f) Tri(ia, ib, ic); else Tri(ia, ic, ib);
            }
            return this;
        }

        /// <summary>
        /// Quad from four corners given as seen from the front: a bottom-left, b bottom-right, c top-right,
        /// d top-left. UVs span uvMin..uvMax. (Unity is left-handed: front faces wind clockwise.)
        /// </summary>
        public MeshKit Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 n = Vector3.Cross(d - a, b - a).normalized;
            int i0 = Add(a, n, new Vector2(uvMin.x, uvMin.y));
            int i1 = Add(b, n, new Vector2(uvMax.x, uvMin.y));
            int i2 = Add(c, n, new Vector2(uvMax.x, uvMax.y));
            int i3 = Add(d, n, new Vector2(uvMin.x, uvMax.y));
            Tri(i0, i2, i1); Tri(i0, i3, i2);
            return this;
        }

        public MeshKit Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => Quad(a, b, c, d, Vector2.zero, Vector2.one);

        /// <summary>Flat quad in the local XZ plane facing +Y, centred on the origin.</summary>
        public MeshKit Ground(float sx, float sz, float uvScale = 1f)
        {
            float x = sx * 0.5f, z = sz * 0.5f;
            return Quad(new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z),
                        Vector2.zero, new Vector2(uvScale, uvScale));
        }

        /// <summary>Upright quad in the local XY plane facing -Z (towards a viewer standing at -Z), centred.</summary>
        public MeshKit Card(float sx, float sy, Vector2 uvMin, Vector2 uvMax, bool doubleSided = false)
        {
            float x = sx * 0.5f, y = sy * 0.5f;
            Quad(new Vector3(-x, -y, 0), new Vector3(x, -y, 0), new Vector3(x, y, 0), new Vector3(-x, y, 0), uvMin, uvMax);
            if (doubleSided)
                Quad(new Vector3(x, -y, 0), new Vector3(-x, -y, 0), new Vector3(-x, y, 0), new Vector3(x, y, 0),
                     new Vector2(uvMax.x, uvMin.y), new Vector2(uvMin.x, uvMax.y));
            return this;
        }

        /// <summary>Axis-aligned box centred on the origin. inside = true flips it into a room seen from within.</summary>
        public MeshKit Box(Vector3 size, bool inside = false, float uvPerMetre = 0f)
        {
            Vector3 h = size * 0.5f;
            Vector3[] p =
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
            foreach (var f in faces)
            {
                Vector3 a = p[f[0]], b = p[f[1]], c = p[f[2]], d = p[f[3]];
                Vector2 uv = uvPerMetre > 0f ? new Vector2((b - a).magnitude, (d - a).magnitude) * uvPerMetre : Vector2.one;
                if (inside) Quad(b, a, d, c, Vector2.zero, uv); else Quad(a, b, c, d, Vector2.zero, uv);
            }
            return this;
        }

        /// <summary>Cylinder or cone frustum along +Y from y = 0 to y = height.</summary>
        public MeshKit Cylinder(float r0, float r1, float height, int seg = 12, bool capBottom = true, bool capTop = true, float uvV = 1f)
        {
            float slope = (r0 - r1) / Mathf.Max(height, 1e-5f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 n0 = new Vector3(d0.x, slope, d0.z).normalized, n1 = new Vector3(d1.x, slope, d1.z).normalized;
                float u0 = (float)i / seg, u1 = (float)(i + 1) / seg;
                int i0 = Add(d0 * r0, n0, new Vector2(u0, 0));
                int i1 = Add(d1 * r0, n1, new Vector2(u1, 0));
                int i2 = Add(d1 * r1 + Vector3.up * height, n1, new Vector2(u1, uvV));
                int i3 = Add(d0 * r1 + Vector3.up * height, n0, new Vector2(u0, uvV));
                Tri(i0, i2, i1); Tri(i0, i3, i2);
            }
            if (capBottom && r0 > 0f) Disc(r0, seg, 0f, false);
            if (capTop && r1 > 0f) Disc(r1, seg, height, true);
            return this;
        }

        /// <summary>Flat disc in XZ at height y. UVs map the disc into the unit square (for a round texture).</summary>
        public MeshKit Disc(float r, int seg = 24, float y = 0f, bool up = true)
        {
            Vector3 n = up ? Vector3.up : Vector3.down;
            int c = Add(new Vector3(0, y, 0), n, new Vector2(0.5f, 0.5f));
            int first = V.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), n, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < seg; i++)
                if (up) Tri(c, first + i + 1, first + i); else Tri(c, first + i, first + i + 1);
            return this;
        }

        /// <summary>UV sphere (or ellipsoid via the transform), centred on the origin.</summary>
        public MeshKit Sphere(float r, int lon = 12, int lat = 8)
        {
            int start = V.Count;
            for (int y = 0; y <= lat; y++)
            {
                float v = (float)y / lat, phi = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = (float)x / lon, th = u * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    Add(d * r, d, new Vector2(u, 1f - v));
                }
            }
            int row = lon + 1;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = start + y * row + x, b = a + 1, c = a + row, d = c + 1;
                    if (y > 0) Tri(a, b, c);
                    if (y < lat - 1) Tri(b, d, c);
                }
            return this;
        }

        /// <summary>Capsule along +Y from y = 0 to y = length (hemispherical ends included in the length).</summary>
        public MeshKit Capsule(float r, float length, int seg = 10)
        {
            var keep = Xf;
            float body = Mathf.Max(0f, length - 2f * r);
            Xf = keep * Matrix4x4.Translate(new Vector3(0, r, 0)); Sphere(r, seg, 6);
            Xf = keep * Matrix4x4.Translate(new Vector3(0, r + body, 0)); Sphere(r, seg, 6);
            Xf = keep * Matrix4x4.Translate(new Vector3(0, r, 0)); Cylinder(r, r, body, seg, false, false);
            Xf = keep;
            return this;
        }

        /// <summary>
        /// A hanging drape: a vertical sheet whose horizontal profile is a sine wave, so a flat velvet texture
        /// reads as deep folds under the fake key light. Spans x in [-w/2, w/2], y in [0, -h] (hangs down), facing -Z.
        /// gather &gt; 0 pinches the bottom towards pinchX (a tied-back stage curtain).
        /// </summary>
        public MeshKit Drape(float w, float h, int folds, float depth, int rows = 6, float gather = 0f, float pinchX = 0f, float uvU = 1f, float uvV = 1f)
        {
            int cols = folds * 4;
            int start = V.Count;
            for (int r = 0; r <= rows; r++)
            {
                float v = (float)r / rows;
                float pinch = gather * v * v;
                for (int c = 0; c <= cols; c++)
                {
                    float u = (float)c / cols;
                    float x = Mathf.Lerp(-w * 0.5f, w * 0.5f, u);
                    x = Mathf.Lerp(x, pinchX, pinch);
                    float ph = u * folds * Mathf.PI * 2f;
                    float z = Mathf.Sin(ph) * depth * (1f - pinch * 0.5f);
                    Vector3 n = new Vector3(Mathf.Cos(ph) * depth * folds * Mathf.PI * 2f / Mathf.Max(w, 1e-4f), 0f, -1f).normalized;
                    Add(new Vector3(x, -h * v, z), n, new Vector2(u * uvU, (1f - v) * uvV));
                }
            }
            int row = cols + 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = start + r * row + c, b = a + 1, d = a + row, e = d + 1;
                    Tri(a, b, e); Tri(a, e, d);     // front
                    Tri(a, e, b); Tri(a, d, e);     // back (same vertices; the shader flips the normal)
                }
            return this;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetColors(C); m.SetTriangles(T, 0);
            if (W.Count == V.Count && W.Count > 0) m.boneWeights = W.ToArray();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Creates a child GameObject carrying this mesh with the given material.</summary>
        public GameObject ToObject(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(name);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            Quiet(mr);
            return go;
        }

        /// <summary>No shadows, no probes, no motion vectors: the fixed PARLOR render budget.</summary>
        public static void Quiet(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
        }
    }
}
