using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Malhas suaves com ar de brinquedo de vinil (concepts): extrusões de contornos arredondados com arestas
    /// boleadas (cabeça triângulo e quadrado), esferas UV lisas, cúpulas e discos. Normais analíticas, sem
    /// facetas. As malhas são partilhadas (cache) por todos os personagens.
    /// </summary>
    public static class ToyMeshes
    {
        static readonly Dictionary<string, Mesh> s_cache = new Dictionary<string, Mesh>();

        static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (s_cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = key;
            s_cache[key] = m;
            return m;
        }

        /// <summary>Cabeça triângulo: prisma de cantos arredondados, base em y=0, frente em +Z.</summary>
        public static Mesh TriangleHead => Cached("ToyTriangleHead", () =>
            RoundedExtrusion(RoundedPolygon(new[] { new Vector2(-0.31f, 0f), new Vector2(0.31f, 0f), new Vector2(0f, 0.53f) }, 0.075f, 10), 0.4f, 0.075f, 6));

        /// <summary>Cabeça quadrado: cubo de cantos e arestas arredondados, base em y=0.</summary>
        public static Mesh SquareHead => Cached("ToySquareHead", () =>
            RoundedExtrusion(RoundedPolygon(new[] { new Vector2(-0.23f, 0f), new Vector2(0.23f, 0f), new Vector2(0.23f, 0.43f), new Vector2(-0.23f, 0.43f) }, 0.085f, 8), 0.4f, 0.08f, 6));

        /// <summary>Esfera UV lisa de raio 0,5 (escalar à vontade).</summary>
        public static Mesh Sphere => Cached("ToySphere", () => UvSphere(28, 18, 1f));

        /// <summary>Meia esfera (cúpula do boné), raio 0,5, base em y=0.</summary>
        public static Mesh Dome => Cached("ToyDome", () => UvSphere(24, 10, 0.5f));

        /// <summary>Caixa de cantos e arestas arredondados, centrada na origem (tronco, pélvis, botas, alças).</summary>
        public static Mesh RoundedBox(float width, float height, float depth, float radius)
        {
            float r = Mathf.Min(radius, width * 0.49f, height * 0.49f, depth * 0.49f);
            string key = "ToyBox_" + width.ToString("0.###") + "_" + height.ToString("0.###") + "_" + depth.ToString("0.###") + "_" + r.ToString("0.###");
            return Cached(key, () =>
            {
                float hw = width * 0.5f, hh = height * 0.5f;
                var outline = RoundedPolygon(new[] { new Vector2(-hw, -hh), new Vector2(hw, -hh), new Vector2(hw, hh), new Vector2(-hw, hh) }, r, 5);
                return RoundedExtrusion(outline, depth, r, 4);
            });
        }

        /// <summary>
        /// Cápsula lisa (membros): pivô no centro do topo, estende-se para baixo (−Y) com comprimento total
        /// <paramref name="length"/>, para rodar a partir da articulação.
        /// </summary>
        public static Mesh Capsule(float radius, float length)
        {
            string key = "ToyCapsule_" + radius.ToString("0.###") + "_" + length.ToString("0.###");
            return Cached(key, () =>
            {
                const int lon = 16, half = 6;
                float r = Mathf.Min(radius, length * 0.5f);
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                int rows = 0;
                for (int part = 0; part < 2; part++)
                {
                    float centerY = part == 0 ? -r : -(length - r);
                    for (int i = 0; i <= half; i++)
                    {
                        float phi = (part == 0 ? 0f : Mathf.PI * 0.5f) + i / (float)half * Mathf.PI * 0.5f;
                        for (int x = 0; x <= lon; x++)
                        {
                            float theta = x / (float)lon * Mathf.PI * 2f;
                            var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                            verts.Add(new Vector3(n.x * r, centerY + n.y * r, n.z * r));
                            norms.Add(n);
                            uvs.Add(new Vector2(x / (float)lon, 1f - rows / (float)(2 * half + 1)));
                        }
                        rows++;
                    }
                }
                for (int y = 0; y < rows - 1; y++)
                    for (int x = 0; x < lon; x++)
                    {
                        int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                        tris.Add(a); tris.Add(b); tris.Add(c);
                        tris.Add(b); tris.Add(d); tris.Add(c);
                    }
                var mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }

        /// <summary>
        /// Contorno 2D convexo (anti-horário) com cantos arredondados de raio <paramref name="radius"/>.
        /// </summary>
        public static Vector2[] RoundedPolygon(Vector2[] corners, float radius, int segments)
        {
            var result = new List<Vector2>();
            int n = corners.Length;
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = corners[(i + n - 1) % n], c = corners[i], next = corners[(i + 1) % n];
                Vector2 inDir = (c - prev).normalized, outDir = (next - c).normalized;
                // Normais para fora (contorno anti-horário → normal à direita da direcção).
                Vector2 nIn = new Vector2(inDir.y, -inDir.x), nOut = new Vector2(outDir.y, -outDir.x);
                // Centro do arco: canto recuado ao longo da bissectriz interior.
                Vector2 bis = (-nIn - nOut).normalized;
                float half = Vector2.Angle(-inDir, outDir) * 0.5f * Mathf.Deg2Rad;
                Vector2 center = c + bis * (radius / Mathf.Sin(half));
                float a0 = Mathf.Atan2(nIn.y, nIn.x), a1 = Mathf.Atan2(nOut.y, nOut.x);
                while (a1 < a0) a1 += Mathf.PI * 2f;
                for (int s = 0; s <= segments; s++)
                {
                    float a = Mathf.Lerp(a0, a1, s / (float)segments);
                    result.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Extrusão em Z de um contorno convexo com arestas boleadas (quarto de círculo de raio <paramref name="bevel"/>)
        /// na frente e atrás; normais suaves nas laterais e planas nas faces.
        /// </summary>
        public static Mesh RoundedExtrusion(Vector2[] outline, float depth, float bevel, int bevelSteps)
        {
            int n = outline.Length;
            var normals2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[(i + n - 1) % n], b = outline[(i + 1) % n];
                Vector2 t = (b - a).normalized;
                normals2[i] = new Vector2(t.y, -t.x);
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Anéis: do centro da face de trás (θ=-90°) até à frente (θ=+90°), passando pela lateral.
            var rings = new List<(float z, float inset, float nz, float nxy)>();
            float halfCore = depth * 0.5f - bevel;
            for (int s = 0; s <= bevelSteps; s++)
            {
                float th = Mathf.Lerp(-Mathf.PI * 0.5f, 0f, s / (float)bevelSteps);
                rings.Add((-halfCore + bevel * Mathf.Sin(th), bevel * (1f - Mathf.Cos(th)), Mathf.Sin(th), Mathf.Cos(th)));
            }
            for (int s = 0; s <= bevelSteps; s++)
            {
                float th = Mathf.Lerp(0f, Mathf.PI * 0.5f, s / (float)bevelSteps);
                rings.Add((halfCore + bevel * Mathf.Sin(th), bevel * (1f - Mathf.Cos(th)), Mathf.Sin(th), Mathf.Cos(th)));
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in outline)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            foreach (var ring in rings)
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = outline[i] - normals2[i] * ring.inset;
                    verts.Add(new Vector3(p.x, p.y, ring.z));
                    norms.Add(new Vector3(normals2[i].x * ring.nxy, normals2[i].y * ring.nxy, ring.nz).normalized);
                    uvs.Add(new Vector2(i / (float)n, Mathf.InverseLerp(minY, maxY, p.y)));
                }
            for (int r = 0; r < rings.Count - 1; r++)
                for (int i = 0; i < n; i++)
                {
                    int a = r * n + i, b = r * n + (i + 1) % n, c = (r + 1) * n + i, d = (r + 1) * n + (i + 1) % n;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }

            // Tampas (faces planas): leque a partir do centro, usando os anéis extremos (normal ±Z).
            Vector2 centroid = Vector2.zero;
            foreach (var p in outline) centroid += p;
            centroid /= n;
            for (int side = 0; side < 2; side++)
            {
                int ringIndex = side == 0 ? 0 : rings.Count - 1;
                float z = rings[ringIndex].z;
                int center = verts.Count;
                verts.Add(new Vector3(centroid.x, centroid.y, z));
                norms.Add(new Vector3(0f, 0f, side == 0 ? -1f : 1f));
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < n; i++)
                {
                    int a = ringIndex * n + i, b = ringIndex * n + (i + 1) % n;
                    if (side == 0) { tris.Add(center); tris.Add(b); tris.Add(a); }
                    else { tris.Add(center); tris.Add(a); tris.Add(b); }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Esfera UV de raio 0,5; <paramref name="latitudeFraction"/> 0,5 dá uma cúpula (hemisfério superior).</summary>
        public static Mesh UvSphere(int lon, int lat, float latitudeFraction)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float phi = Mathf.PI * (1f - v * latitudeFraction); // de baixo (π) para cima (0)
                if (latitudeFraction < 1f) phi = Mathf.PI * 0.5f * (1f - v);
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    var nrm = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    verts.Add(nrm * 0.5f);
                    norms.Add(nrm);
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            if (latitudeFraction < 1f)
            {
                // Fecha a base da cúpula.
                int center = verts.Count;
                verts.Add(Vector3.zero);
                norms.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int x = 0; x < lon; x++) { tris.Add(center); tris.Add(x); tris.Add(x + 1); }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
