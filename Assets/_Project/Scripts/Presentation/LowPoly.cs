using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Malhas low-poly facetadas (normais planas) geradas em código: membros, troncos, cabeças e peças
    /// mecânicas. Todas cacheadas por parâmetros para não duplicar malhas por personagem.
    /// </summary>
    public static class LowPoly
    {
        static readonly Dictionary<string, Mesh> s_cache = new Dictionary<string, Mesh>();

        sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<int> _t = new List<int>();
            readonly List<Vector2> _uv = new List<Vector2>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = _v.Count;
                _v.Add(a);
                _v.Add(b);
                _v.Add(c);
                _uv.Add(new Vector2(0, 0));
                _uv.Add(new Vector2(1, 0));
                _uv.Add(new Vector2(0, 1));
                _t.Add(i);
                _t.Add(i + 1);
                _t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public Mesh Build(string name, bool flipWinding = false)
            {
                // Unity desenha a face cujo sentido é horário visto de fora; flipWinding corrige malhas geradas ao contrário.
                if (flipWinding)
                    for (int i = 0; i < _t.Count; i += 3)
                        (_t[i + 1], _t[i + 2]) = (_t[i + 2], _t[i + 1]);
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Mesh Cached(string key, System.Func<Mesh> create)
        {
            if (!s_cache.TryGetValue(key, out var mesh) || mesh == null) s_cache[key] = mesh = create();
            return mesh;
        }

        /// <summary>
        /// Membro/tronco: prisma de N lados que desce do pivô (Y=0) até −length, com tampas.
        /// Raio de topo e de base independentes (afunilado) e escala em Z para achatar.
        /// </summary>
        public static Mesh Limb(int sides, float topRadius, float bottomRadius, float length, float depthScale = 1f)
        {
            string key = "limb" + sides + "_" + topRadius + "_" + bottomRadius + "_" + length + "_" + depthScale;
            return Cached(key, () =>
            {
                var b = new Builder();
                var top = new Vector3[sides];
                var bottom = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                    float x = Mathf.Cos(a), z = Mathf.Sin(a) * depthScale;
                    top[i] = new Vector3(x * topRadius, 0f, z * topRadius);
                    bottom[i] = new Vector3(x * bottomRadius, -length, z * bottomRadius);
                }
                var topCenter = Vector3.zero;
                var bottomCenter = new Vector3(0f, -length, 0f);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    b.Quad(top[i], top[j], bottom[j], bottom[i]);
                    b.Tri(topCenter, top[j], top[i]);
                    b.Tri(bottomCenter, bottom[i], bottom[j]);
                }
                return b.Build("Limb");
            });
        }

        /// <summary>Caixa facetada com chanfro (bevel) — mãos, pés, peças do revólver.</summary>
        public static Mesh BevelBox(float bevel = 0.18f)
        {
            return Cached("bevelbox2_" + bevel, () =>
            {
                // Octógono extrudido em Z com as arestas cortadas: lê-se como caixa chanfrada low-poly.
                var b = new Builder();
                float e = 0.5f, c = 0.5f - bevel;
                var ring = new[]
                {
                    new Vector2(-c, -e), new Vector2(c, -e), new Vector2(e, -c), new Vector2(e, c),
                    new Vector2(c, e), new Vector2(-c, e), new Vector2(-e, c), new Vector2(-e, -c)
                };
                float zf = -0.5f, zb = 0.5f, zfi = -c, zbi = c;
                for (int i = 0; i < 8; i++)
                {
                    int j = (i + 1) % 8;
                    var a0 = new Vector3(ring[i].x, ring[i].y, zfi);
                    var a1 = new Vector3(ring[j].x, ring[j].y, zfi);
                    var b0 = new Vector3(ring[i].x, ring[i].y, zbi);
                    var b1 = new Vector3(ring[j].x, ring[j].y, zbi);
                    b.Quad(a0, b0, b1, a1);
                    // Chanfro frontal e traseiro.
                    var f0 = new Vector3(ring[i].x * (1f - bevel * 2f), ring[i].y * (1f - bevel * 2f), zf);
                    var f1 = new Vector3(ring[j].x * (1f - bevel * 2f), ring[j].y * (1f - bevel * 2f), zf);
                    var k0 = new Vector3(ring[i].x * (1f - bevel * 2f), ring[i].y * (1f - bevel * 2f), zb);
                    var k1 = new Vector3(ring[j].x * (1f - bevel * 2f), ring[j].y * (1f - bevel * 2f), zb);
                    b.Quad(f0, a0, a1, f1);
                    b.Quad(b0, k0, k1, b1);
                    b.Tri(new Vector3(0, 0, zf), f0, f1);
                    b.Tri(new Vector3(0, 0, zb), k1, k0);
                }
                // O octógono e os chanfros foram escritos no sentido anti-horário (faces para dentro): inverte.
                return b.Build("BevelBox", true);
            });
        }

        /// <summary>Icosfera de subdivisão baixa — cabeça "Círculo" facetada, olhos, gosma.</summary>
        public static Mesh Icosphere(int subdivisions)
        {
            return Cached("ico" + subdivisions, () =>
            {
                float t = (1f + Mathf.Sqrt(5f)) / 2f;
                var verts = new List<Vector3>
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
                };
                for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized * 0.5f;
                var faces = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
                };
                for (int s = 0; s < subdivisions; s++)
                {
                    var next = new List<int>();
                    var midCache = new Dictionary<long, int>();
                    int Mid(int a, int c)
                    {
                        long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                        if (midCache.TryGetValue(key, out int idx)) return idx;
                        verts.Add(((verts[a] + verts[c]) * 0.5f).normalized * 0.5f);
                        midCache[key] = verts.Count - 1;
                        return verts.Count - 1;
                    }
                    for (int f = 0; f < faces.Count; f += 3)
                    {
                        int a = faces[f], b2 = faces[f + 1], c = faces[f + 2];
                        int ab = Mid(a, b2), bc = Mid(b2, c), ca = Mid(c, a);
                        next.AddRange(new[] { a, ab, ca, b2, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    faces = next;
                }
                var b = new Builder();
                // A tabela clássica já está no sentido horário visto de fora (frente no Unity). Invertê-la deixava a
                // esfera do avesso: só se viam as faces de trás (cartão "Círculo" da roleta, centro da roda).
                for (int f = 0; f < faces.Count; f += 3) b.Tri(verts[faces[f]], verts[faces[f + 1]], verts[faces[f + 2]]);
                return b.Build("Icosphere");
            });
        }

        /// <summary>Anel de N lados (guarda-mato do gatilho, cachecol). Centro na origem, eixo Z.</summary>
        public static Mesh Ring(int sides, float radius, float thickness, float width, float arcDegrees = 360f)
        {
            string key = "ring" + sides + "_" + radius + "_" + thickness + "_" + width + "_" + arcDegrees;
            return Cached(key, () =>
            {
                var b = new Builder();
                float arc = arcDegrees * Mathf.Deg2Rad;
                for (int i = 0; i < sides; i++)
                {
                    float a0 = arc * i / sides, a1 = arc * (i + 1) / sides;
                    Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    float ro = radius + thickness * 0.5f, ri = radius - thickness * 0.5f, hw = width * 0.5f;
                    var o0 = Dir(a0) * ro; var o1 = Dir(a1) * ro; var i0 = Dir(a0) * ri; var i1 = Dir(a1) * ri;
                    var f = new Vector3(0, 0, -hw); var k = new Vector3(0, 0, hw);
                    b.Quad(o0 + f, o1 + f, o1 + k, o0 + k);
                    b.Quad(i0 + k, i1 + k, i1 + f, i0 + f);
                    b.Quad(i0 + f, i1 + f, o1 + f, o0 + f);
                    b.Quad(o0 + k, o1 + k, i1 + k, i0 + k);
                }
                return b.Build("Ring");
            });
        }

        /// <summary>Mancha irregular achatada (poça de gosma) — disco de N lados com raio aleatório.</summary>
        public static Mesh Blob(int seed)
        {
            return Cached("blob" + seed, () =>
            {
                var rng = new System.Random(seed);
                const int sides = 14;
                var b = new Builder();
                var ring = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    float r = 0.35f + (float)rng.NextDouble() * 0.2f;
                    ring[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                }
                var top = new Vector3(0f, 0.06f, 0f);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    var mid0 = ring[i] * 0.65f + Vector3.up * 0.045f;
                    var mid1 = ring[j] * 0.65f + Vector3.up * 0.045f;
                    b.Quad(ring[i], mid0, mid1, ring[j]);
                    b.Tri(mid0, top, mid1);
                }
                return b.Build("Blob");
            });
        }

        public static GameObject Part(Mesh mesh, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, List<Renderer> track = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            track?.Add(r);
            return go;
        }

        public static Transform Pivot(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }
    }
}
