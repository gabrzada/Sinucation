using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Texturas procedurais (albedo + normal map) geradas uma vez no arranque: feltro, betão molhado,
    /// painéis metálicos com rebites, riscas de perigo, grade, madeira. Respeitam a qualidade de textura
    /// e o filtro anisotrópico escolhidos nas definições gráficas.
    /// </summary>
    public static class TextureFactory
    {
        public struct Pair
        {
            public Texture2D Albedo;
            public Texture2D Normal;
        }

        static readonly Dictionary<string, Pair> s_cache = new Dictionary<string, Pair>();

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144269504;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Ruído de valor tileable (periodo = cells).</summary>
        static float Noise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int X(int i) => ((i % cells) + cells) % cells;
            float a = Hash(X(x0), X(y0), seed), b = Hash(X(x0 + 1), X(y0), seed);
            float c = Hash(X(x0), X(y0 + 1), seed), d = Hash(X(x0 + 1), X(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, cells, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        /// <summary>
        /// Versão em alta resolução gerada offline (Blender/textures.py → Resources/Textures/FS/&lt;nome&gt;_albedo|_normal.bytes,
        /// PNG). Lida em runtime, sem depender das definições de importação do Unity.
        /// </summary>
        static bool TryLoadBaked(string key, out Pair pair)
        {
            pair = default;
            try
            {
                var albedoBytes = Resources.Load<TextAsset>("Textures/FS/" + key + "_albedo");
                var normalBytes = Resources.Load<TextAsset>("Textures/FS/" + key + "_normal");
                if (albedoBytes == null || normalBytes == null) return false;
                var albedo = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = key + "_Albedo", wrapMode = TextureWrapMode.Repeat };
                var normal = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { name = key + "_Normal", wrapMode = TextureWrapMode.Repeat };
                if (!albedo.LoadImage(albedoBytes.bytes, false) || !normal.LoadImage(normalBytes.bytes, false)) return false;
                albedo.wrapMode = TextureWrapMode.Repeat;
                normal.wrapMode = TextureWrapMode.Repeat;
                albedo.Apply(true, true);
                normal.Apply(true, true);
                GraphicsQuality.ConfigureTexture(albedo);
                GraphicsQuality.ConfigureTexture(normal);
                pair = new Pair { Albedo = albedo, Normal = normal };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("FriendSlop: textura " + key + " não carregou (" + e.Message + ") — a gerar a procedural.");
                return false;
            }
        }

        static Pair Create(string key, int size, float bumpStrength, Func<float, float, (Color color, float height)> sample)
        {
            if (s_cache.TryGetValue(key, out var cached) && cached.Albedo != null) return cached;
            if (TryLoadBaked(key, out var baked))
            {
                s_cache[key] = baked;
                return baked;
            }
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = key + "_Albedo", wrapMode = TextureWrapMode.Repeat };
            var normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = key + "_Normal", wrapMode = TextureWrapMode.Repeat };
            var colors = new Color32[size * size];
            var heights = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var s = sample((x + 0.5f) / size, (y + 0.5f) / size);
                    colors[y * size + x] = s.color;
                    heights[y * size + x] = s.height;
                }
            var normals = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float hl = heights[y * size + (x - 1 + size) % size], hr = heights[y * size + (x + 1) % size];
                    float hd = heights[((y - 1 + size) % size) * size + x], hu = heights[((y + 1) % size) * size + x];
                    var n = new Vector3((hl - hr) * bumpStrength, (hd - hu) * bumpStrength, 1f).normalized;
                    // Formato compatível com UnpackNormal (DXT5nm/AG): X em A e R=1, Y em G.
                    byte nx = (byte)Mathf.Clamp((n.x * 0.5f + 0.5f) * 255f, 0f, 255f);
                    byte ny = (byte)Mathf.Clamp((n.y * 0.5f + 0.5f) * 255f, 0f, 255f);
                    normals[y * size + x] = new Color32(255, ny, 0, nx);
                }
            albedo.SetPixels32(colors);
            albedo.Apply(true, false);
            normal.SetPixels32(normals);
            normal.Apply(true, false);
            GraphicsQuality.ConfigureTexture(albedo);
            GraphicsQuality.ConfigureTexture(normal);
            var pair = new Pair { Albedo = albedo, Normal = normal };
            s_cache[key] = pair;
            return pair;
        }

        public static void ReapplyFiltering()
        {
            foreach (var p in s_cache.Values)
            {
                GraphicsQuality.ConfigureTexture(p.Albedo);
                GraphicsQuality.ConfigureTexture(p.Normal);
            }
        }

        public static Pair Felt() => Create("felt", 256, 2f, (u, v) =>
        {
            float n = Fbm(u, v, 32, 3, 1);
            float fibers = Noise(u * 3f, v * 0.5f, 96, 2) * 0.5f;
            return (Color.Lerp(ArtPalette.Felt * 0.9f, ArtPalette.Felt * 1.08f, n * 0.7f + fibers * 0.3f), n * 0.4f + fibers * 0.2f);
        });

        public static Pair Concrete() => Create("concrete", 512, 6f, (u, v) =>
        {
            float n = Fbm(u, v, 8, 5, 3);
            float spots = Mathf.SmoothStep(0.55f, 0.75f, Fbm(u, v, 4, 3, 9));
            float seamU = Mathf.Min(Mathf.Repeat(u * 2f, 1f), 1f - Mathf.Repeat(u * 2f, 1f));
            float seamV = Mathf.Min(Mathf.Repeat(v * 2f, 1f), 1f - Mathf.Repeat(v * 2f, 1f));
            float seam = Mathf.SmoothStep(0.006f, 0.0f, Mathf.Min(seamU, seamV));
            var baseColor = Color.Lerp(new Color(0.16f, 0.17f, 0.2f), new Color(0.24f, 0.24f, 0.26f), n);
            baseColor = Color.Lerp(baseColor, new Color(0.11f, 0.12f, 0.15f), spots * 0.6f);
            baseColor = Color.Lerp(baseColor, new Color(0.07f, 0.07f, 0.08f), seam);
            return (baseColor, n * 0.5f - seam * 0.6f);
        });

        public static Pair MetalPanel() => Create("metalpanel", 512, 5f, (u, v) =>
        {
            float n = Fbm(u, v, 16, 4, 5);
            float pu = Mathf.Repeat(u * 2f, 1f), pv = Mathf.Repeat(v * 2f, 1f);
            float edge = Mathf.Min(Mathf.Min(pu, 1f - pu), Mathf.Min(pv, 1f - pv));
            float seam = Mathf.SmoothStep(0.012f, 0f, edge);
            float bevel = Mathf.SmoothStep(0.05f, 0.012f, edge);
            float rivet = 0f;
            foreach (float cx in new[] { 0.06f, 0.94f })
                foreach (float cy in new[] { 0.06f, 0.5f, 0.94f })
                    rivet = Mathf.Max(rivet, Mathf.SmoothStep(0.022f, 0.01f, Vector2.Distance(new Vector2(pu, pv), new Vector2(cx, cy))));
            float wear = Mathf.SmoothStep(0.62f, 0.8f, Fbm(u, v, 12, 4, 11)) * bevel;
            var c = Color.Lerp(ArtPalette.Navy * 0.85f, ArtPalette.NavyLight, n * 0.4f);
            c = Color.Lerp(c, new Color(0.55f, 0.5f, 0.42f), wear * 0.6f);
            c = Color.Lerp(c, new Color(0.05f, 0.06f, 0.08f), seam);
            c = Color.Lerp(c, ArtPalette.IvoryShade * 0.7f, rivet);
            return (c, n * 0.2f + rivet * 0.8f - seam * 0.5f + bevel * 0.2f);
        });

        public static Pair Hazard() => Create("hazard", 256, 3f, (u, v) =>
        {
            float stripe = Mathf.Repeat((u + v) * 4f, 1f) < 0.5f ? 1f : 0f;
            float n = Fbm(u, v, 16, 4, 13);
            float wear = Mathf.SmoothStep(0.6f, 0.78f, n);
            var c = stripe > 0.5f ? ArtPalette.Mustard : ArtPalette.NavyDark;
            c = Color.Lerp(c, new Color(0.45f, 0.42f, 0.38f), wear * 0.7f);
            return (c * (0.9f + n * 0.2f), n * 0.3f + wear * 0.2f);
        });

        public static Pair WornMustard() => Create("wornmustard", 256, 3f, (u, v) =>
        {
            float n = Fbm(u, v, 12, 4, 17);
            float chip = Mathf.SmoothStep(0.64f, 0.7f, Fbm(u, v, 20, 3, 19));
            var c = Color.Lerp(ArtPalette.Mustard, ArtPalette.MustardDark, n * 0.5f);
            c = Color.Lerp(c, ArtPalette.NavyDark, chip * 0.85f);
            return (c, n * 0.2f - chip * 0.4f);
        });

        public static Pair Grate() => Create("grate", 256, 6f, (u, v) =>
        {
            float gu = Mathf.Repeat(u * 16f, 1f), gv = Mathf.Repeat(v * 16f, 1f);
            float bar = (gu < 0.18f || gv < 0.18f) ? 1f : 0f;
            float n = Fbm(u, v, 8, 3, 23);
            var c = bar > 0.5f ? Color.Lerp(new Color(0.28f, 0.3f, 0.34f), new Color(0.4f, 0.4f, 0.42f), n) : new Color(0.02f, 0.02f, 0.03f);
            return (c, bar);
        });

        public static Pair Wood() => Create("wood", 256, 3f, (u, v) =>
        {
            float grain = Mathf.Sin((u * 40f + Fbm(u, v, 4, 4, 29) * 6f) * Mathf.PI) * 0.5f + 0.5f;
            float plank = Mathf.Repeat(v * 4f, 1f) < 0.02f ? 1f : 0f;
            var c = Color.Lerp(new Color(0.32f, 0.18f, 0.1f), new Color(0.48f, 0.28f, 0.15f), grain);
            c = Color.Lerp(c, new Color(0.1f, 0.05f, 0.03f), plank);
            return (c, grain * 0.3f - plank * 0.8f);
        });

        public static Pair Crate() => Create("crate", 256, 4f, (u, v) =>
        {
            float grain = Mathf.Sin((v * 30f + Fbm(u, v, 4, 3, 31) * 5f) * Mathf.PI) * 0.5f + 0.5f;
            float border = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) < 0.1f ? 1f : 0f;
            float diag = Mathf.Abs(u - v) < 0.06f ? 1f : 0f;
            float frame = Mathf.Max(border, diag);
            var c = Color.Lerp(new Color(0.42f, 0.29f, 0.17f), new Color(0.55f, 0.39f, 0.23f), grain);
            c = Color.Lerp(c, c * 0.75f, frame);
            return (c, grain * 0.2f + frame * 0.6f);
        });
    }
}
