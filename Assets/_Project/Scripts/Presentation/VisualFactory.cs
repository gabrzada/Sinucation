using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Materiais, texturas e malhas procedurais para o protótipo (sem dependência de assets de arte).</summary>
    public static class VisualFactory
    {
        static Material s_litTemplate;
        static Material s_unlitTemplate;
        static Shader s_celShader;
        static readonly Dictionary<Material, Material> s_sceneMaterials = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, Shader> s_originalShaders = new Dictionary<Material, Shader>();
        static int s_style;

        static Shader CelShader => s_celShader != null ? s_celShader : (s_celShader = Resources.Load<Shader>("Shaders/CelLit"));

        /// <summary>Copies imported Lit materials so style changes never modify project assets.</summary>
        public static Material SceneMaterial(Material source)
        {
            if (source == null || source.shader == null || source.shader.name != "Universal Render Pipeline/Lit" || CelShader == null)
                return source;
            if (s_sceneMaterials.TryGetValue(source, out var cached) && cached != null) return cached;
            var material = new Material(source) { name = source.name + " (scene)" };
            RegisterStyle(material);
            s_sceneMaterials[source] = material;
            return material;
        }

        static void RegisterStyle(Material material)
        {
            s_originalShaders[material] = material.shader;
            ApplyMaterialStyle(material, material.shader);
        }

        static void ApplyMaterialStyle(Material material, Shader original)
        {
            bool transparent = material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f;
            var target = s_style > 0 && !transparent && CelShader != null ? CelShader : original;
            if (material.shader != target)
            {
                // Nesta versão do Unity atribuir um shader apaga as keywords do material (_EMISSION, _NORMALMAP…):
                // sem as repor, lâmpadas, néons e clarabóias deixavam de brilhar e as texturas perdiam o relevo.
                var keywords = material.shaderKeywords;
                material.shader = target;
                foreach (var keyword in keywords) material.EnableKeyword(keyword);
#if UNITY_EDITOR
                // URP 17.7 checks Meta readiness on registration, but not on material updates.
                // Prepare the new variant before Surface Cache GI observes this shader change.
                material.DisableKeyword("EDITOR_VISUALIZATION");
                int meta = material.FindPass("Meta");
                if (meta >= 0) UnityEditor.ShaderUtil.CompilePass(material, meta, true);
#endif
            }
            if (original.name == "Universal Render Pipeline/Complex Lit" && s_style == 0)
                material.EnableKeyword("_CLEARCOAT");
        }

        public static void ForgetMaterial(Material material) => s_originalShaders.Remove(material);

        public static void ApplyStyle(int style)
        {
            s_style = Mathf.Clamp(style, 0, 2);
            Shader.SetGlobalFloat("_FSCelStyle", s_style);
            var expired = new List<Material>();
            foreach (var pair in s_originalShaders)
                if (pair.Key == null) expired.Add(pair.Key);
                else ApplyMaterialStyle(pair.Key, pair.Value);
            foreach (var material in expired) s_originalShaders.Remove(material);
        }

        /// <summary>Cores do jogo standard (iguais às texturas de Blender/ball_textures.py).</summary>
        public static readonly Color[] BallColors =
        {
            new Color32(246, 242, 230, 255), // 0 branca
            new Color32(250, 192, 0, 255),   // 1 amarelo
            new Color32(20, 64, 168, 255),   // 2 azul
            new Color32(214, 34, 30, 255),   // 3 vermelho
            new Color32(84, 40, 146, 255),   // 4 roxo
            new Color32(244, 106, 18, 255),  // 5 laranja
            new Color32(14, 124, 66, 255),   // 6 verde
            new Color32(122, 22, 30, 255),   // 7 bordô
            new Color32(20, 20, 22, 255),    // 8 preta
        };

        public static void Initialize(Material litTemplate, Material unlitTemplate)
        {
            s_litTemplate = litTemplate;
            s_unlitTemplate = unlitTemplate;
        }

        public static Color BallColor(int number)
        {
            if (number <= 0) return BallColors[0];
            return BallColors[((number - 1) % 8) + 1];
        }

        public static bool IsStripe(int number) => number >= 9;

        public static Material Lit(Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            Material m;
            if (s_litTemplate != null) m = new Material(s_litTemplate);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                m = new Material(shader);
            }
            SetColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            RegisterStyle(m);
            return m;
        }

        public static Material Unlit(Color color)
        {
            Material m;
            if (s_unlitTemplate != null) m = new Material(s_unlitTemplate);
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                m = new Material(shader);
            }
            SetColor(m, color);
            return m;
        }

        public static void SetColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        /// <summary>Alterna o material URP/Lit entre opaco e transparente (fantasmas de espectador).</summary>
        public static void SetTransparent(Material m, bool on)
        {
            if (!m.HasProperty("_Surface")) return;
            m.SetFloat("_Surface", on ? 1f : 0f);
            if (s_originalShaders.TryGetValue(m, out var original)) ApplyMaterialStyle(m, original);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", on ? "Transparent" : "Opaque");
            m.SetFloat("_SrcBlend", on ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", on ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : (float)UnityEngine.Rendering.BlendMode.Zero);
            m.SetFloat("_ZWrite", on ? 0f : 1f);
            if (on) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = on ? (int)UnityEngine.Rendering.RenderQueue.Transparent : -1;
        }

        static Material s_complexTemplate;

        public static void InitializeComplex(Material complexLitTemplate) => s_complexTemplate = complexLitTemplate;

        /// <summary>Material com albedo + normal map procedurais e repetição (tiling).</summary>
        public static Material Textured(TextureFactory.Pair pair, Vector2 tiling, float smoothness, float metallic = 0f, float bump = 1f, Color? tint = null)
        {
            var m = Lit(tint ?? Color.white, smoothness, metallic);
            SetTexture(m, pair.Albedo);
            if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", pair.Normal);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling);
            if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", tiling);
            return m;
        }

        /// <summary>Bolas de bilhar: URP Complex Lit com verniz (clear coat) quando disponível.</summary>
        public static Material ClearCoat(Texture texture)
        {
            Material m;
            if (s_complexTemplate != null) m = new Material(s_complexTemplate);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Complex Lit");
                m = shader != null ? new Material(shader) : Lit(Color.white, 0.9f);
            }
            SetColor(m, Color.white);
            SetTexture(m, texture);
            // Verniz mais contido: o reflexo das luzes já não tapa o número quando a bola passa por baixo de um candeeiro.
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.56f);
            if (m.HasProperty("_ClearCoatMask"))
            {
                m.SetFloat("_ClearCoat", 1f);
                m.SetFloat("_ClearCoatMask", 0.45f);
                m.SetFloat("_ClearCoatSmoothness", 0.78f);
                m.EnableKeyword("_CLEARCOAT");
            }
            RegisterStyle(m);
            return m;
        }

        public static void SetEmission(Material m, Color emission)
        {
            if (!m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;
            m.SetColor("_EmissionColor", emission);
        }

        /// <summary>Material emissivo (néons, lâmpadas, ecrãs) — brilha com o bloom.</summary>
        public static Material Emissive(Color color, float intensity)
        {
            var m = Lit(color * 0.3f, 0.8f);
            SetEmission(m, color * intensity);
            return m;
        }

        static Material s_particleTemplate;

        public static void InitializeParticles(Material particleTemplate) => s_particleTemplate = particleTemplate;

        /// <summary>Material de partículas URP (unlit, transparente, com a cor de vértice das partículas).</summary>
        public static Material ParticleMaterial(Color color)
        {
            Material m;
            if (s_particleTemplate != null) m = new Material(s_particleTemplate);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) return Unlit(color);
                m = new Material(shader);
                SetTransparent(m, true);
            }
            SetColor(m, color);
            // Sem textura as partículas desenham-se como quadrados: disco suave com queda gaussiana.
            SetTexture(m, SoftDot);
            return m;
        }

        static Texture2D s_softDot;

        public static Texture2D SoftDot
        {
            get
            {
                if (s_softDot != null) return s_softDot;
                const int n = 64;
                s_softDot = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
                s_softDot.SetPixels(px);
                s_softDot.Apply(true, true);
                return s_softDot;
            }
        }

        static Font s_titleFont;
        static Font s_bodyFont;

        public static Font TitleFont => s_titleFont != null ? s_titleFont : (s_titleFont = Resources.Load<Font>("Fonts/Anton-Regular"));
        public static Font BodyFont => s_bodyFont != null ? s_bodyFont : (s_bodyFont = Resources.Load<Font>("Fonts/Oswald"));

        static readonly Dictionary<Font, Material> s_textMaterials = new Dictionary<Font, Material>();
        static bool s_fontHooked;

        /// <summary>
        /// Material de texto com teste de profundidade (o do TextMesh desenha-se por cima de tudo). A textura da
        /// fonte dinâmica é recriada quando entram caracteres novos: acompanha o evento textureRebuilt.
        /// </summary>
        static Material TextMaterial(Font font)
        {
            if (s_textMaterials.TryGetValue(font, out var m) && m != null) return m;
            var shader = Resources.Load<Shader>("Shaders/Text3D");
            if (shader == null) return font.material;
            m = new Material(shader) { name = "Text3D_" + font.name, mainTexture = font.material.mainTexture };
            s_textMaterials[font] = m;
            if (!s_fontHooked)
            {
                s_fontHooked = true;
                Font.textureRebuilt += rebuilt =>
                {
                    if (rebuilt != null && s_textMaterials.TryGetValue(rebuilt, out var mat) && mat != null) mat.mainTexture = rebuilt.material.mainTexture;
                };
            }
            return m;
        }

        /// <summary>Texto 3D no mundo (placas das máquinas) com a fonte do projecto.</summary>
        public static TextMesh Text(Transform parent, string text, Vector3 localPosition, float height, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool title = true)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var tm = go.AddComponent<TextMesh>();
            var font = title ? TitleFont : BodyFont;
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(font);
            }
            tm.text = text;
            tm.fontSize = 96;
            tm.characterSize = height / 9.6f;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            return tm;
        }

        static Mesh s_prism;
        static Mesh s_cone;

        public static Mesh TriangularPrismShared => s_prism != null ? s_prism : (s_prism = TriangularPrism(0.34f, 0.3f));
        public static Mesh ConeShared => s_cone != null ? s_cone : (s_cone = Cone(1f, 1f, 18));

        /// <summary>Cone com base no Y=0 e ponta em Y=height.</summary>
        public static Mesh Cone(float radius, float height, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var apex = new Vector3(0, height, 0);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                int v = verts.Count;
                verts.Add(p0);
                verts.Add(apex);
                verts.Add(p1);
                tris.Add(v);
                tris.Add(v + 1);
                tris.Add(v + 2);
                v = verts.Count;
                verts.Add(Vector3.zero);
                verts.Add(p0);
                verts.Add(p1);
                tris.Add(v);
                tris.Add(v + 1);
                tris.Add(v + 2);
            }
            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void SetTexture(Material m, Texture texture)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        // ------------------------------------------------------------------ bolas

        static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
        };

        static readonly Dictionary<int, Texture2D> s_ballTextures = new Dictionary<int, Texture2D>();

        /// <summary>
        /// Textura da bola: a importada de Resources/Textures/Balls (números projectados sem distorção, 1024×512, gerada
        /// por Blender/ball_textures.py); se faltar, a versão procedural de reserva.
        /// </summary>
        public static Texture2D BallTexture(int number)
        {
            if (s_ballTextures.TryGetValue(number, out var cached) && cached != null) return cached;
            var imported = Resources.Load<Texture2D>("Textures/Balls/ball_" + number);
            var tex = imported != null ? imported : ProceduralBallTexture(number);
            if (imported == null) tex.ignoreMipmapLimit = true;
            tex.anisoLevel = 8;
            s_ballTextures[number] = tex;
            return tex;
        }

        static Texture2D ProceduralBallTexture(int number)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Ball" + number, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[w * h];
            Color baseColor = BallColor(number);
            bool stripe = IsStripe(number);
            Color white = new Color(0.97f, 0.96f, 0.92f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    if (number == 0) c = white;
                    else if (stripe) c = (y > h * 0.3f && y < h * 0.7f) ? baseColor : white;
                    else c = baseColor;
                    pixels[y * w + x] = c;
                }
            }

            if (number > 0)
            {
                DrawNumberSpot(pixels, w, h, w / 4, h / 2, number);
                DrawNumberSpot(pixels, w, h, 3 * w / 4, h / 2, number);
            }
            else
            {
                DrawDisc(pixels, w, h, w / 4, h / 2, 5, new Color(0.85f, 0.15f, 0.15f));
            }

            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        static void DrawNumberSpot(Color32[] pixels, int w, int h, int cx, int cy, int number)
        {
            DrawDisc(pixels, w, h, cx, cy, 19, new Color(0.97f, 0.96f, 0.92f));
            string text = number.ToString();
            const int scale = 4;
            int glyphW = 3 * scale, glyphH = 5 * scale, spacing = scale;
            int totalW = text.Length * glyphW + (text.Length - 1) * spacing;
            int startX = cx - totalW / 2;
            int startY = cy - glyphH / 2;
            var ink = new Color32(20, 20, 20, 255);

            for (int i = 0; i < text.Length; i++)
            {
                string glyph = Digits[text[i] - '0'];
                int gx0 = startX + i * (glyphW + spacing);
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (glyph[row * 3 + col] != '1') continue;
                        for (int py = 0; py < scale; py++)
                            for (int px = 0; px < scale; px++)
                            {
                                int x = gx0 + col * scale + px;
                                int y = startY + (4 - row) * scale + py;
                                if (x >= 0 && x < w && y >= 0 && y < h) pixels[y * w + x] = ink;
                            }
                    }
            }
        }

        static void DrawDisc(Color32[] pixels, int w, int h, int cx, int cy, int radius, Color color)
        {
            Color32 c = color;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
                    int wx = (x % w + w) % w;
                    if (y >= 0 && y < h) pixels[y * w + wx] = c;
                }
        }

        // ------------------------------------------------------------------ malhas

        /// <summary>Prisma triangular (cabeça do arquétipo Triângulo), centrado na origem, com normais planas.</summary>
        public static Mesh TriangularPrism(float size, float depth)
        {
            float half = size * 0.5f;
            float d = depth * 0.5f;
            var top = new Vector3(0f, half, 0f);
            var left = new Vector3(-half, -half * 0.8f, 0f);
            var right = new Vector3(half, -half * 0.8f, 0f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                tris.Add(i);
                tris.Add(i + 1);
                tris.Add(i + 2);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e)
            {
                Face(a, b, c);
                Face(a, c, e);
            }

            var fz = new Vector3(0, 0, -d);
            var bz = new Vector3(0, 0, d);
            Face(top + fz, right + fz, left + fz);
            Face(top + bz, left + bz, right + bz);
            Quad(left + fz, right + fz, right + bz, left + bz);
            Quad(top + fz, left + fz, left + bz, top + bz);
            Quad(right + fz, top + fz, top + bz, right + bz);

            var mesh = new Mesh { name = "TrianglePrism" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
