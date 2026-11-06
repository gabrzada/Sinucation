using System;
using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Encaixe automático dos chapéus na geometria real de cada cabeça: procura a escala (X/Z independentes, para a
    /// cabeça triangular em cunha) e a altura mais baixas em que NENHUM vértice da cabeça atravessa o chapéu, sem
    /// tapar os olhos. O resultado fica em cache por forma de cabeça e chapéu.
    /// </summary>
    public static class HatFit
    {
        public sealed class Shape
        {
            public string Key;
            /// <summary>Altura interior do chapéu (em unidades do modelo).</summary>
            public float Height;
            /// <summary>Sem tampa (coroa): a cabeça pode sair por cima.</summary>
            public bool OpenTop;
            /// <summary>Raio interior à altura h acima da base.</summary>
            public Func<float, float> Inner;
            /// <summary>Quanto o chapéu deve descer abaixo do topo da cabeça para parecer "vestido".</summary>
            public float MinDepth;
        }

        public struct Result
        {
            public Vector3 Position;
            public Vector3 Scale;
        }

        static readonly Dictionary<string, Result> s_cache = new Dictionary<string, Result>();
        static readonly Dictionary<Archetype, Vector3[]> s_heads = new Dictionary<Archetype, Vector3[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            s_cache.Clear();
            s_heads.Clear();
        }

        public static readonly Shape TopHat = new Shape { Key = "tophat", Height = 0.29f, Inner = h => 0.15f, MinDepth = 0.03f };
        public static readonly Shape PartyHat = new Shape { Key = "party", Height = 0.33f, Inner = h => 0.14f * (1f - h / 0.34f), MinDepth = 0.02f };
        public static readonly Shape Crown = new Shape { Key = "crown", Height = 0.07f, OpenTop = true, Inner = h => 0.148f, MinDepth = 0.03f };
        public static readonly Shape Beanie = new Shape { Key = "beanie", Height = 0.24f, Inner = BeanieInner, MinDepth = 0.07f };
        public static readonly Shape Cap = new Shape { Key = "cap", Height = 0.17f, Inner = h => 0.228f * Mathf.Sqrt(Mathf.Max(0f, 1f - (h / 0.18f) * (h / 0.18f))), MinDepth = 0.04f };

        static readonly Vector2[] BeanieProfile = { new Vector2(0.245f, 0f), new Vector2(0.245f, 0.04f), new Vector2(0.235f, 0.1f), new Vector2(0.2f, 0.17f), new Vector2(0.15f, 0.22f), new Vector2(0.08f, 0.245f), new Vector2(0f, 0.25f) };

        static float BeanieInner(float h)
        {
            for (int i = 1; i < BeanieProfile.Length; i++)
            {
                var a = BeanieProfile[i - 1];
                var b = BeanieProfile[i];
                if (h <= b.y) return Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, h)) - 0.012f;
            }
            return 0f;
        }

        /// <summary>Vértices da cabeça no espaço "Shape" do AvatarVisual (antes da HeadScale).</summary>
        static Vector3[] HeadVertices(Archetype a)
        {
            if (s_heads.TryGetValue(a, out var cached)) return cached;
            Vector3[] v;
            switch (a)
            {
                case Archetype.Triangle: v = ToyMeshes.TriangleHead.vertices; break;
                case Archetype.Square: v = ToyMeshes.SquareHead.vertices; break;
                default:
                    v = ToyMeshes.Sphere.vertices;
                    for (int i = 0; i < v.Length; i++) v[i] = v[i] * 0.47f + new Vector3(0f, 0.235f, 0f);
                    break;
            }
            s_heads[a] = v;
            return v;
        }

        /// <summary>
        /// Posição e escala do chapéu. <paramref name="tiltX"/> é a inclinação do chapéu (graus, eixo X) e
        /// <paramref name="forward"/> o avanço em Z; <paramref name="minY"/> é a altura mínima da base (olhos visíveis).
        /// </summary>
        public static Result Fit(Archetype archetype, Shape hat, float top, float minY, float tiltX = 0f, float forward = 0f)
        {
            string key = archetype + "|" + hat.Key + "|" + top.ToString("0.###") + "|" + minY.ToString("0.###") + "|" + tiltX.ToString("0.#");
            if (s_cache.TryGetValue(key, out var cached)) return cached;

            var verts = HeadVertices(archetype);
            // Topo real da malha (o arredondamento baixa o bico do triângulo em relação ao valor nominal).
            float meshTop = float.NegativeInfinity;
            foreach (var v in verts) meshTop = Mathf.Max(meshTop, v.y);
            top = meshTop;
            var inv = Quaternion.Inverse(Quaternion.Euler(tiltX, 0f, 0f));
            var candidates = new List<Vector2>();
            for (float sx = 1f; sx <= 1.701f; sx += 0.05f)
                for (float sz = 1f; sz <= 1.701f; sz += 0.05f)
                    candidates.Add(new Vector2(sx, sz));
            // Primeiro as escalas menores e mais uniformes.
            candidates.Sort((p, q) =>
            {
                int c = (p.x + p.y).CompareTo(q.x + q.y);
                return c != 0 ? c : Mathf.Abs(p.x - p.y).CompareTo(Mathf.Abs(q.x - q.y));
            });

            var best = new Result { Position = new Vector3(0f, top, forward), Scale = Vector3.one };
            float bestDepth = float.NegativeInfinity;
            Result? flush = null;
            foreach (var c in candidates)
            {
                float sy = Mathf.Max(c.x, c.y);
                var scale = new Vector3(c.x, sy, c.y);
                float lowest = float.NaN;
                for (float y = top + 0.01f; y >= Mathf.Max(minY, top - 0.45f); y -= 0.005f)
                {
                    if (!Fits(verts, new Vector3(0f, y, forward), inv, scale, hat)) break;
                    lowest = y;
                }
                if (float.IsNaN(lowest)) continue;
                float depth = top - lowest;
                if (flush == null && depth >= -0.001f) flush = new Result { Position = new Vector3(0f, lowest, forward), Scale = scale };
                if (depth > bestDepth + 0.004f)
                {
                    bestDepth = depth;
                    best = new Result { Position = new Vector3(0f, lowest, forward), Scale = scale };
                }
                if (depth >= hat.MinDepth) break;
            }
            // Se não dá para "vestir" (ex.: coroa numa cabeça quadrada larga), fica assente rente ao topo com o menor tamanho.
            if (bestDepth < hat.MinDepth && flush.HasValue) best = flush.Value;
            s_cache[key] = best;
            return best;
        }

        static bool Fits(Vector3[] verts, Vector3 pivot, Quaternion inv, Vector3 s, Shape hat)
        {
            const float margin = 0.006f;
            for (int i = 0; i < verts.Length; i++)
            {
                var l = inv * (verts[i] - pivot);
                if (l.y <= 0.002f) continue;
                float h = l.y / s.y;
                if (h > hat.Height)
                {
                    if (hat.OpenTop) continue;
                    return false;
                }
                float r = hat.Inner(h) - margin;
                if (r <= 0f) return false;
                float x = l.x / s.x, z = l.z / s.z;
                if (x * x + z * z > r * r) return false;
            }
            return true;
        }
    }
}
