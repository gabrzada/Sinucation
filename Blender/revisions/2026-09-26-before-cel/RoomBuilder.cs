using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Arena industrial (concept 29): armazém com betão molhado, plataforma circular com anel de perigo,
    /// paredes de painel metálico, varanda com público, logótipo em néon, candeeiros industriais com feixes,
    /// luzes de feira, bandeiras e adereços. Os colliders servem só o movimento social e a gosma.
    /// </summary>
    public sealed class RoomBuilder : MonoBehaviour
    {
        public const float HalfX = 7.5f;
        public const float HalfZ = 6.5f;
        public const float Height = 6f;
        const float BalconyY = 2.9f;

        static readonly Vector3[] Spawns =
        {
            new Vector3(-3f, 0.05f, -3f), new Vector3(3f, 0.05f, -3f), new Vector3(-3.4f, 0.05f, 2.4f), new Vector3(3.4f, 0.05f, 2.4f),
            new Vector3(0f, 0.05f, -3.2f), new Vector3(-4.5f, 0.05f, -0.5f), new Vector3(4.2f, 0.05f, 0.8f), new Vector3(2.2f, 0.05f, -3.2f),
        };

        public static Vector3 SpawnPoint(int index) => Spawns[((index % Spawns.Length) + Spawns.Length) % Spawns.Length];

        public static Vector3 ClampInside(Vector3 p, float minY, float maxY)
        {
            p.x = Mathf.Clamp(p.x, -HalfX + 0.3f, HalfX - 0.3f);
            p.z = Mathf.Clamp(p.z, -HalfZ + 0.3f, HalfZ - 0.3f);
            p.y = Mathf.Clamp(p.y, minY, maxY);
            return p;
        }

        /// <summary>Lugares à volta da mesa onde os bots esperam a vez.</summary>
        public static Vector3 HomeSlot(int index, int count)
        {
            float t = count <= 1 ? 0.5f : index / (float)count;
            float angle = t * Mathf.PI * 2f + 0.4f;
            return new Vector3(Mathf.Cos(angle) * 2.4f, 0f, Mathf.Sin(angle) * 1.85f);
        }

        // Âncoras para as máquinas (frente das máquinas em −Z local).
        public static Pose RandomizerAnchor => new Pose(new Vector3(0f, 0f, HalfZ - 1.25f), Quaternion.identity);
        public static Pose SabotageAnchor => new Pose(new Vector3(HalfX - 1.3f, 0f, -1.2f), Quaternion.Euler(0f, 90f, 0f));
        public static Pose JukeboxAnchor => new Pose(new Vector3(-HalfX + 0.7f, 0f, 3.2f), Quaternion.Euler(0f, -90f, 0f));

        readonly List<Light> _shadowLights = new List<Light>();
        public IReadOnlyList<Light> ShadowLights => _shadowLights;
        public ParticleSystem Dust { get; private set; }

        public void Build(float tableHalfLength, float tableHalfWidth)
        {
            var root = transform;
            var concrete = VisualFactory.Textured(TextureFactory.Concrete(), new Vector2(HalfX * 2f / 3f, HalfZ * 2f / 3f), 0.42f, 0.05f, 0.35f);
            var panel = VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(HalfX * 2f / 2.5f, Height / 2.5f), 0.45f, 0.4f, 1f);
            var panelSide = VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(HalfZ * 2f / 2.5f, Height / 2.5f), 0.45f, 0.4f, 1f);
            var hazard = VisualFactory.Textured(TextureFactory.Hazard(), new Vector2(HalfX * 2f / 1.2f, 1f), 0.4f, 0.1f, 1f);
            var hazardSide = VisualFactory.Textured(TextureFactory.Hazard(), new Vector2(HalfZ * 2f / 1.2f, 1f), 0.4f, 0.1f, 1f);
            var mustard = VisualFactory.Textured(TextureFactory.WornMustard(), Vector2.one, 0.5f, 0.35f, 1f);
            var navy = VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(1f, 1f), 0.4f, 0.35f, 1f);
            var grate = VisualFactory.Textured(TextureFactory.Grate(), new Vector2(HalfX * 2f, 1.2f), 0.5f, 0.6f, 1f);
            var ceiling = VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(HalfX * 2f / 2.5f, HalfZ * 2f / 2.5f), 0.3f, 0.3f, 0.6f, new Color(0.35f, 0.36f, 0.42f));
            var box = LowPoly.BevelBox(0.06f);

            // Chão, tecto e paredes.
            Solid(root, "Floor", new Vector3(0, -0.05f, 0), new Vector3(HalfX * 2f, 0.1f, HalfZ * 2f), concrete);
            // O tecto não projecta sombra: a luz-chave entra pelas clarabóias.
            Solid(root, "Ceiling", new Vector3(0, Height + 0.05f, 0), new Vector3(HalfX * 2f, 0.1f, HalfZ * 2f), ceiling).GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Solid(root, "WallN", new Vector3(0, Height * 0.5f, HalfZ + 0.1f), new Vector3(HalfX * 2f, Height, 0.2f), panel);
            Solid(root, "WallS", new Vector3(0, Height * 0.5f, -HalfZ - 0.1f), new Vector3(HalfX * 2f, Height, 0.2f), panel);
            Solid(root, "WallE", new Vector3(HalfX + 0.1f, Height * 0.5f, 0), new Vector3(0.2f, Height, HalfZ * 2f), panelSide);
            Solid(root, "WallW", new Vector3(-HalfX - 0.1f, Height * 0.5f, 0), new Vector3(0.2f, Height, HalfZ * 2f), panelSide);
            VisualFactory.Primitive(PrimitiveType.Cube, "HazardN", root, new Vector3(0, 0.45f, HalfZ - 0.005f), new Vector3(HalfX * 2f, 0.9f, 0.02f), hazard);
            VisualFactory.Primitive(PrimitiveType.Cube, "HazardS", root, new Vector3(0, 0.45f, -HalfZ + 0.005f), new Vector3(HalfX * 2f, 0.9f, 0.02f), hazard);
            VisualFactory.Primitive(PrimitiveType.Cube, "HazardE", root, new Vector3(HalfX - 0.005f, 0.45f, 0), new Vector3(0.02f, 0.9f, HalfZ * 2f), hazardSide);
            VisualFactory.Primitive(PrimitiveType.Cube, "HazardW", root, new Vector3(-HalfX + 0.005f, 0.45f, 0), new Vector3(0.02f, 0.9f, HalfZ * 2f), hazardSide);

            // Plataforma circular sob a mesa com anel de perigo.
            // UVs planares (em vez das radiais do cilindro): as chapas e riscos da textura não esticam em raios.
            var platformMesh = Object.Instantiate(LowPoly.Limb(40, 3.1f, 3.1f, 0.004f));
            var platformVerts = platformMesh.vertices;
            var platformUv = new Vector2[platformVerts.Length];
            for (int i = 0; i < platformVerts.Length; i++) platformUv[i] = new Vector2(platformVerts[i].x, platformVerts[i].z) * 0.4f;
            platformMesh.uv = platformUv;
            // Plataforma rente ao chão (1,2 cm): antes tinha 6 cm só visuais e os pés de todos ficavam enterrados.
            LowPoly.Part(platformMesh, "Platform", root, new Vector3(0f, 0.004f, 0f), Vector3.one, navy);
            var ring = LowPoly.Part(LowPoly.Ring(48, 3.05f, 0.18f, 0.004f), "PlatformRing", root, new Vector3(0f, 0.005f, 0f), Vector3.one, mustard);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var innerRing = LowPoly.Part(LowPoly.Ring(48, 2.6f, 0.03f, 0.004f), "PlatformLine", root, new Vector3(0f, 0.006f, 0f), Vector3.one, VisualFactory.Emissive(ArtPalette.Mustard, 1.5f));
            innerRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Colisão da mesa (para ninguém a atravessar).
            var tableBlock = new GameObject("TableCollider");
            tableBlock.transform.SetParent(root, false);
            tableBlock.transform.localPosition = new Vector3(0, 0.425f, 0);
            tableBlock.AddComponent<BoxCollider>().size = new Vector3(tableHalfLength * 2f + 0.3f, 0.85f, tableHalfWidth * 2f + 0.3f);

            BuildBalcony(root, grate, mustard, navy, box);
            BuildLogoWall(root, box);
            BuildSkylights(root);
            BuildCeilingLamps(root, mustard);
            BuildLamps(root, mustard);
            BuildStringLights(root);
            BuildWallLamps(root);
            BuildBanners(root);
            BuildProps(root, box, mustard, navy);
            BuildBar(root);
            BuildLounge(root);
            BuildStorage(root);
            BuildPipes(root);

            // Ambiente: bastante mais claro e quente (os concepts são uma arena bem iluminada, não uma cave).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.56f, 0.6f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.4f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.21f, 0.19f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.3f, 0.23f, 0.24f);
            RenderSettings.fogDensity = 0.007f;

            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(root, false);
            probeGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(HalfX * 2f, Height, HalfZ * 2f);
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.intensity = 1f;
            Probe = probe;
        }

        public ReflectionProbe Probe { get; private set; }

        void BuildBalcony(Transform root, Material grate, Material mustard, Material navy, Mesh box)
        {
            // Varanda em "U" (fundo e laterais) com gradeamento mostarda e colunas.
            float depth = 1.5f;
            Solid(root, "BalconyN", new Vector3(0f, BalconyY, HalfZ - depth * 0.5f), new Vector3(HalfX * 2f, 0.12f, depth), grate);
            Solid(root, "BalconyE", new Vector3(HalfX - depth * 0.5f, BalconyY, 1.2f), new Vector3(depth, 0.12f, HalfZ * 2f - 2.4f - depth), grate);
            Solid(root, "BalconyW", new Vector3(-HalfX + depth * 0.5f, BalconyY, 1.2f), new Vector3(depth, 0.12f, HalfZ * 2f - 2.4f - depth), grate);
            var rail = VisualFactory.Lit(ArtPalette.Mustard, 0.6f, 0.4f);
            void Rail(Vector3 a, Vector3 b)
            {
                Vector3 mid = (a + b) * 0.5f;
                float len = Vector3.Distance(a, b);
                var go = LowPoly.Part(LowPoly.Limb(8, 0.035f, 0.035f, len), "Rail", root, mid, Vector3.one, rail);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (b - a).normalized);
                go.transform.localPosition = a;
            }
            float zEdge = HalfZ - depth;
            float xEdge = HalfX - depth;
            float zStart = 1.2f - (HalfZ * 2f - 2.4f - depth) * 0.5f;
            foreach (float h in new[] { 0.55f, 1.0f })
            {
                Rail(new Vector3(-xEdge, BalconyY + h, zEdge), new Vector3(xEdge, BalconyY + h, zEdge));
                Rail(new Vector3(xEdge, BalconyY + h, zEdge), new Vector3(xEdge, BalconyY + h, zStart));
                Rail(new Vector3(-xEdge, BalconyY + h, zEdge), new Vector3(-xEdge, BalconyY + h, zStart));
            }
            for (int i = 0; i <= 8; i++)
            {
                float x = Mathf.Lerp(-xEdge, xEdge, i / 8f);
                LowPoly.Part(LowPoly.Limb(8, 0.03f, 0.03f, 1f), "Post", root, new Vector3(x, BalconyY + 1f, zEdge), Vector3.one, rail);
            }
            foreach (float x in new[] { -xEdge, xEdge })
                foreach (float z in new[] { zEdge, zStart, (zEdge + zStart) * 0.5f })
                {
                    LowPoly.Part(box, "Column", root, new Vector3(x, BalconyY * 0.5f, z), new Vector3(0.28f, BalconyY, 0.28f), navy).AddComponent<BoxCollider>();
                    LowPoly.Part(box, "ColumnBand", root, new Vector3(x, 0.5f, z), new Vector3(0.32f, 0.3f, 0.32f), mustard);
                }
            BalconySpots = new List<Vector3>();
            for (int i = 0; i < 9; i++) BalconySpots.Add(new Vector3(Mathf.Lerp(-xEdge + 0.6f, xEdge - 0.6f, i / 8f), BalconyY + 0.06f, zEdge + 0.5f));
            for (int i = 0; i < 3; i++)
            {
                BalconySpots.Add(new Vector3(xEdge + 0.5f, BalconyY + 0.06f, Mathf.Lerp(zStart + 0.6f, zEdge - 0.6f, i / 2f)));
                BalconySpots.Add(new Vector3(-xEdge - 0.5f, BalconyY + 0.06f, Mathf.Lerp(zStart + 0.6f, zEdge - 0.6f, i / 2f)));
            }
        }

        /// <summary>Lugares do público na varanda.</summary>
        public List<Vector3> BalconySpots { get; private set; }

        void BuildLogoWall(Transform root, Mesh box)
        {
            float z = HalfZ - 0.04f;
            float y = 4.7f;
            var backing = LowPoly.Part(box, "LogoBacking", root, new Vector3(0f, y, z), new Vector3(5.6f, 1.9f, 0.08f), VisualFactory.Lit(ArtPalette.NavyDark, 0.3f));
            backing.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mustardNeon = VisualFactory.Emissive(ArtPalette.Mustard, 6f);
            var coralNeon = VisualFactory.Emissive(ArtPalette.Coral, 6f);
            var tealNeon = VisualFactory.Emissive(ArtPalette.Teal, 6f);
            // Triângulo em néon.
            var tri = new[] { new Vector3(-2.05f, y - 0.55f, z - 0.08f), new Vector3(-1.35f, y + 0.55f, z - 0.08f), new Vector3(-0.65f, y - 0.55f, z - 0.08f) };
            for (int i = 0; i < 3; i++) Tube(root, tri[i], tri[(i + 1) % 3], mustardNeon);
            // Círculo em néon.
            var circle = LowPoly.Part(LowPoly.Ring(28, 0.58f, 0.07f, 0.07f), "NeonCircle", root, new Vector3(0f, y, z - 0.08f), Vector3.one, coralNeon);
            circle.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Quadrado em néon.
            var sq = new[] { new Vector3(0.75f, y - 0.55f, z - 0.08f), new Vector3(0.75f, y + 0.55f, z - 0.08f), new Vector3(1.85f, y + 0.55f, z - 0.08f), new Vector3(1.85f, y - 0.55f, z - 0.08f) };
            for (int i = 0; i < 4; i++) Tube(root, sq[i], sq[(i + 1) % 4], tealNeon);
            AddLight(root, "LogoGlowL", new Vector3(-1.35f, y, z - 0.6f), LightType.Point, ArtPalette.Mustard, 2f, 4f, false);
            AddLight(root, "LogoGlowC", new Vector3(0f, y, z - 0.6f), LightType.Point, ArtPalette.Coral, 2f, 4f, false);
            AddLight(root, "LogoGlowR", new Vector3(1.3f, y, z - 0.6f), LightType.Point, ArtPalette.Teal, 2f, 4f, false);
            VisualFactory.Text(root, "FRIEND SLOP", new Vector3(0f, y - 1.2f, z - 0.1f), 0.5f, ArtPalette.Ivory).transform.localRotation = Quaternion.identity;

            // Néons nas paredes laterais (△○□ pequenos).
            var neonTexts = new[] { ("8", new Vector3(HalfX - 0.05f, 3.9f, -3.5f), Quaternion.Euler(0f, 90f, 0f)), ("8", new Vector3(-HalfX + 0.05f, 4.2f, -3f), Quaternion.Euler(0f, -90f, 0f)) };
            foreach (var (text, pos, rot) in neonTexts)
            {
                var tm = VisualFactory.Text(root, text, pos, 0.9f, ArtPalette.Teal);
                tm.transform.rotation = rot;
            }
        }

        static void Tube(Transform root, Vector3 a, Vector3 b, Material mat)
        {
            var go = LowPoly.Part(LowPoly.Limb(6, 0.035f, 0.035f, Vector3.Distance(a, b)), "Neon", root, a, Vector3.one, mat);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (b - a).normalized);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Candeeiro pendente: cúpula lisa (ToyMeshes), aro escuro e difusor luminoso por baixo. A luz em si é
        /// uma fonte real (Light) — nada de "feixes" geométricos a fingir volume.
        /// </summary>
        void PendantLamp(Transform root, Vector3 position, float size, Material shade, Material diffuser, Material trim, Material cord)
        {
            LowPoly.Part(LowPoly.Limb(8, 0.012f, 0.012f, Height - position.y - size * 0.5f), "Cord", root, new Vector3(position.x, Height, position.z), Vector3.one, cord);
            var dome = LowPoly.Part(ToyMeshes.Dome, "LampShade", root, position, new Vector3(size, size * 0.62f, size), shade);
            dome.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rim = LowPoly.Part(LowPoly.Ring(28, size * 0.5f, 0.03f, 0.04f), "LampRim", root, position + Vector3.down * 0.01f, Vector3.one, trim);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rim.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var glow = LowPoly.Part(ToyMeshes.Sphere, "LampDiffuser", root, position + Vector3.down * 0.015f, new Vector3(size * 0.9f, 0.03f, size * 0.9f), diffuser);
            glow.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildLamps(Transform root, Material mustard)
        {
            // Três candeeiros de mesa (concept): cúpula mostarda, difusor que brilha e spot real sobre o pano.
            var diffuser = VisualFactory.Emissive(new Color(1f, 0.9f, 0.7f), 3.5f);
            var cord = VisualFactory.Lit(new Color(0.05f, 0.05f, 0.05f), 0.3f);
            var trim = VisualFactory.Lit(ArtPalette.NavyDark, 0.5f, 0.4f);
            var shade = VisualFactory.Lit(ArtPalette.Mustard, 0.55f, 0.3f);
            float lampY = 2.35f;
            float[] xs = { -0.95f, 0f, 0.95f };
            for (int i = 0; i < 3; i++)
            {
                PendantLamp(root, new Vector3(xs[i], lampY, 0f), 0.62f, shade, diffuser, trim, cord);
                var light = AddLight(root, "TableLight" + i, new Vector3(xs[i], lampY - 0.06f, 0f), LightType.Spot, new Color(1f, 0.9f, 0.74f), 4.2f, 5f, i == 1, 110f);
                if (i == 1) _shadowLights.Add(light);
            }
            // Pó a flutuar nos feixes (partículas com disco suave).
            var dustGo = new GameObject("Dust");
            dustGo.transform.SetParent(root, false);
            dustGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Dust = dustGo.AddComponent<ParticleSystem>();
            Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = Dust.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0.02f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.014f);
            main.maxParticles = 250;
            main.startColor = new Color(1f, 0.92f, 0.75f, 0.35f);
            var emission = Dust.emission;
            emission.rateOverTime = 25f;
            var shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3f, 1.6f, 1.4f);
            var noise = Dust.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 0.3f;
            var renderer = dustGo.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = VisualFactory.ParticleMaterial(new Color(1f, 0.95f, 0.85f, 0.4f));
            Dust.Play();
        }

        /// <summary>Clarabóias no tecto + luz-chave direccional quente (com sombras) que entra por elas.</summary>
        void BuildSkylights(Transform root)
        {
            var sky = VisualFactory.Emissive(new Color(0.75f, 0.82f, 1f), 1.6f);
            var frame = VisualFactory.Lit(ArtPalette.NavyDark, 0.4f, 0.4f);
            for (int i = 0; i < 3; i++)
            {
                float x = -4.5f + i * 4.5f;
                LowPoly.Part(LowPoly.BevelBox(0.05f), "SkylightFrame", root, new Vector3(x, Height - 0.02f, -1.5f), new Vector3(2.4f, 0.06f, 1.6f), frame)
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                LowPoly.Part(LowPoly.BevelBox(0.02f), "Skylight", root, new Vector3(x, Height - 0.05f, -1.5f), new Vector3(2.2f, 0.02f, 1.4f), sky)
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(root, false);
            keyGo.transform.localRotation = Quaternion.Euler(62f, -35f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.9f, 0.78f);
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;
            _shadowLights.Add(key);
            RenderSettings.sun = key;
        }

        /// <summary>Candeeiros industriais de tecto (cúpula + lâmpada) com spot real cada um: iluminam toda a arena.</summary>
        void BuildCeilingLamps(Transform root, Material mustard)
        {
            var diffuser = VisualFactory.Emissive(new Color(1f, 0.88f, 0.66f), 3f);
            var cord = VisualFactory.Lit(new Color(0.05f, 0.05f, 0.05f), 0.3f);
            var trim = VisualFactory.Lit(ArtPalette.NavyDark, 0.5f, 0.4f);
            var shade = VisualFactory.Lit(ArtPalette.Mustard, 0.5f, 0.35f);
            float y = Height - 1.0f;
            for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 2; iz++)
            {
                var p = new Vector3(-4.6f + ix * 4.6f, y, iz == 0 ? -3.4f : 3.2f);
                PendantLamp(root, p, 0.95f, shade, diffuser, trim, cord);
                AddLight(root, "CeilingLight", p + Vector3.down * 0.08f, LightType.Spot, new Color(1f, 0.86f, 0.66f), 9f, 9f, false, 125f);
            }
        }
        void BuildStringLights(Transform root)
        {
            Color[] colors = { new Color(1f, 0.78f, 0.45f), new Color(1f, 0.45f, 0.4f), new Color(0.45f, 0.85f, 1f), new Color(1f, 0.85f, 0.35f) };
            var mats = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++) mats[i] = VisualFactory.Emissive(colors[i], 5f);
            var cord = VisualFactory.Lit(new Color(0.05f, 0.05f, 0.05f), 0.3f);
            for (int row = 0; row < 4; row++)
            {
                float z = Mathf.Lerp(-HalfZ + 1f, HalfZ - 1.8f, row / 3f);
                Vector3 a = new Vector3(-HalfX + 0.4f, Height - 0.6f, z), b = new Vector3(HalfX - 0.4f, Height - 0.6f, z);
                const int bulbs = 22;
                for (int i = 0; i <= bulbs; i++)
                {
                    float t = i / (float)bulbs;
                    var p = Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * 0.7f;
                    LowPoly.Part(LowPoly.Icosphere(0), "Bulb", root, p, Vector3.one * 0.08f, mats[(i + row) % mats.Length])
                        .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (i < bulbs)
                    {
                        var q = Vector3.Lerp(a, b, (i + 1f) / bulbs) + Vector3.down * Mathf.Sin((i + 1f) / bulbs * Mathf.PI) * 0.7f;
                        Tube(root, p, q, cord);
                    }
                    // Algumas lâmpadas da grinalda iluminam mesmo (luz colorida suave).
                    if (i % 6 == 3) AddLight(root, "StringLight", p + Vector3.down * 0.1f, LightType.Point, colors[(i + row) % colors.Length], 0.9f, 3.2f, false);
                }
            }
        }

        /// <summary>Candeeiros de parede (Kenney) com luz própria ao longo das paredes laterais e de trás.</summary>
        void BuildWallLamps(Transform root)
        {
            // "Wall washers": luz alta junto às paredes para as paredes, a varanda e o tecto não ficarem pretos.
            Color[] wash = { new Color(1f, 0.72f, 0.45f), new Color(0.75f, 0.55f, 1f), new Color(1f, 0.6f, 0.5f), new Color(0.5f, 0.85f, 1f) };
            int w = 0;
            for (int i = 0; i < 4; i++)
            {
                float x = Mathf.Lerp(-HalfX + 1.5f, HalfX - 1.5f, i / 3f);
                AddLight(root, "WallWashN", new Vector3(x, 4.2f, HalfZ - 1.2f), LightType.Point, wash[w++ % wash.Length], 3.2f, 7f, false);
                AddLight(root, "WallWashS", new Vector3(x, 3.6f, -HalfZ + 1.2f), LightType.Point, wash[w++ % wash.Length], 2.6f, 7f, false);
            }
            foreach (float z in new[] { -3f, 1.5f })
            {
                AddLight(root, "WallWashE", new Vector3(HalfX - 1.2f, 4f, z), LightType.Point, wash[w++ % wash.Length], 2.6f, 7f, false);
                AddLight(root, "WallWashW", new Vector3(-HalfX + 1.2f, 4f, z), LightType.Point, wash[w++ % wash.Length], 2.6f, 7f, false);
            }

            (Vector3 pos, float yaw)[] lamps =
            {
                (new Vector3(-HalfX + 0.02f, 2.2f, -1.2f), 90f), (new Vector3(-HalfX + 0.02f, 2.2f, 1.6f), 90f),
                (new Vector3(HalfX - 0.02f, 2.2f, 1.6f), -90f), (new Vector3(HalfX - 0.02f, 2.2f, -3.6f), -90f),
                (new Vector3(-2.5f, 2.2f, -HalfZ + 0.02f), 0f), (new Vector3(2.5f, 2.2f, -HalfZ + 0.02f), 0f),
            };
            var sconceShade = VisualFactory.Lit(ArtPalette.Mustard, 0.55f, 0.3f);
            var sconceGlow = VisualFactory.Emissive(new Color(1f, 0.85f, 0.6f), 3f);
            var bracket = VisualFactory.Lit(ArtPalette.NavyDark, 0.5f, 0.5f);
            foreach (var (pos, yaw) in lamps)
            {
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var sconce = LowPoly.Pivot("WallSconce", root, pos);
                sconce.localRotation = Quaternion.Euler(0f, yaw, 0f);
                LowPoly.Part(ToyMeshes.RoundedBox(0.12f, 0.2f, 0.04f, 0.015f), "Plate", sconce, new Vector3(0f, 0f, 0.02f), Vector3.one, bracket);
                LowPoly.Part(ToyMeshes.RoundedBox(0.035f, 0.035f, 0.22f, 0.012f), "Arm", sconce, new Vector3(0f, 0.02f, 0.13f), Vector3.one, bracket);
                LowPoly.Part(ToyMeshes.Dome, "Shade", sconce, new Vector3(0f, 0.02f, 0.24f), new Vector3(0.24f, 0.16f, 0.24f), sconceShade)
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                LowPoly.Part(ToyMeshes.Sphere, "Glow", sconce, new Vector3(0f, 0.012f, 0.24f), new Vector3(0.21f, 0.02f, 0.21f), sconceGlow)
                    .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                AddLight(root, "WallLamp", pos + dir * 0.26f + Vector3.down * 0.06f, LightType.Point, new Color(1f, 0.78f, 0.52f), 1.8f, 4.5f, false);
            }
        }
        void BuildBanners(Transform root)
        {
            var mustardCloth = VisualFactory.Lit(ArtPalette.Mustard, 0.1f);
            var violetCloth = VisualFactory.Lit(ArtPalette.Violet, 0.1f);
            var navyCloth = VisualFactory.Lit(ArtPalette.Navy, 0.1f);
            var box = LowPoly.BevelBox(0.02f);
            (Vector3 pos, Material cloth, string icon, Color iconColor)[] banners =
            {
                (new Vector3(-4.2f, 4.6f, HalfZ - 0.1f), mustardCloth, "8", ArtPalette.NavyDark),
                (new Vector3(4.2f, 4.6f, HalfZ - 0.1f), violetCloth, "?", ArtPalette.Ivory),
                (new Vector3(-5.8f, 4.6f, HalfZ - 0.1f), navyCloth, "△", ArtPalette.Mustard),
                (new Vector3(5.8f, 4.6f, HalfZ - 0.1f), navyCloth, "□", ArtPalette.Teal),
            };
            foreach (var b in banners)
            {
                LowPoly.Part(box, "Banner", root, b.pos, new Vector3(1.1f, 1.8f, 0.02f), b.cloth);
                LowPoly.Part(VisualFactory.TriangularPrismShared, "BannerTail", root, b.pos + new Vector3(0f, -1.05f, 0f), new Vector3(3.2f, -0.9f, 0.05f), b.cloth);
                var iconMat = VisualFactory.Lit(b.iconColor, 0.3f);
                if (b.icon == "△")
                    LowPoly.Part(VisualFactory.TriangularPrismShared, "BannerIcon", root, b.pos + new Vector3(0f, 0.1f, -0.03f), new Vector3(1.9f, 1.8f, 0.05f), iconMat);
                else if (b.icon == "□")
                    LowPoly.Part(box, "BannerIcon", root, b.pos + new Vector3(0f, 0.1f, -0.03f), new Vector3(0.6f, 0.6f, 0.02f), iconMat);
                else
                    VisualFactory.Text(root, b.icon, b.pos + new Vector3(0f, 0.05f, -0.03f), 0.9f, b.iconColor);
            }
        }

        void BuildProps(Transform root, Mesh box, Material mustard, Material navy)
        {
            BuildGumballMachine(root, new Vector3(-6.6f, 0f, -1.8f));
            BuildBallVending(root, new Vector3(-5.2f, 0f, 5.55f));

            var rng = new System.Random(5);
            foreach (var p in new[] { new Vector3(-3.6f, 0f, 4.6f), new Vector3(3.9f, 0f, 4.4f), new Vector3(4.9f, 0f, -2.6f) })
                PropLibrary.Spawn("Factory/cone", root, p, (float)rng.NextDouble() * 360f, 0.55f, colliderInset: 0.1f);
            PropLibrary.Spawn("Factory/warning-traffic", root, new Vector3(-3.2f, 0f, 5.6f), 20f, 1.2f);
        }

        static Material Glass()
        {
            // Shader próprio (Resources/Shaders/Glass): o URP Lit transparente ficava leitoso e escondia o conteúdo.
            var shader = Resources.Load<Shader>("Shaders/Glass");
            if (shader != null) return new Material(shader) { name = "Glass" };
            var glass = VisualFactory.Lit(new Color(0.85f, 0.93f, 1f, 0.1f), 0.6f);
            VisualFactory.SetTransparent(glass, true);
            return glass;
        }
        /// <summary>Máquina de pastilhas: pedestal vermelho, mecanismo de moedas, globo de vidro cheio de bolas e tampa.</summary>
        void BuildGumballMachine(Transform root, Vector3 at)
        {
            var red = VisualFactory.Lit(ArtPalette.Red, 0.55f, 0.1f);
            var chrome = VisualFactory.Lit(new Color(0.82f, 0.82f, 0.86f), 0.9f, 1f);
            var dark = VisualFactory.Lit(ArtPalette.NavyDark, 0.5f);
            var m = LowPoly.Pivot("GumballMachine", root, at);
            m.localRotation = Quaternion.Euler(0f, 90f, 0f); // frente virada para o centro da sala
            LowPoly.Part(ToyMeshes.RoundedBox(0.5f, 0.85f, 0.5f, 0.08f), "Pedestal", m, new Vector3(0f, 0.425f, 0f), Vector3.one, red);
            LowPoly.Part(ToyMeshes.RoundedBox(0.58f, 0.06f, 0.58f, 0.025f), "Foot", m, new Vector3(0f, 0.03f, 0f), Vector3.one, chrome);
            LowPoly.Part(ToyMeshes.RoundedBox(0.3f, 0.26f, 0.06f, 0.03f), "CoinBox", m, new Vector3(0f, 0.72f, 0.27f), Vector3.one, chrome);
            LowPoly.Part(ToyMeshes.RoundedBox(0.04f, 0.1f, 0.02f, 0.008f), "Slot", m, new Vector3(0.08f, 0.76f, 0.305f), Vector3.one, dark);
            var dial = LowPoly.Part(LowPoly.Limb(18, 0.06f, 0.06f, 0.05f), "Dial", m, new Vector3(-0.06f, 0.72f, 0.3f), Vector3.one, chrome);
            dial.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            LowPoly.Part(ToyMeshes.RoundedBox(0.16f, 0.1f, 0.1f, 0.03f), "Chute", m, new Vector3(0f, 0.5f, 0.27f), Vector3.one, dark);
            LowPoly.Part(LowPoly.Ring(28, 0.2f, 0.05f, 0.06f), "Collar", m, new Vector3(0f, 0.89f, 0f), Vector3.one, chrome).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Bolas dentro do globo (metade de baixo cheia).
            var rng = new System.Random(21);
            Vector3 globeCenter = new Vector3(0f, 1.24f, 0f);
            for (int i = 0; i < 26; i++)
            {
                Vector3 p;
                do p = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f) * 0.27f;
                while (p.sqrMagnitude > 0.27f * 0.27f || p.y > 0.04f);
                var ball = VisualFactory.Lit(VisualFactory.BallColor(1 + rng.Next(8)), 0.75f);
                LowPoly.Part(ToyMeshes.Sphere, "Gumball", m, globeCenter + p, Vector3.one * 0.1f, ball);
            }
            LowPoly.Part(ToyMeshes.Sphere, "Globe", m, globeCenter, Vector3.one * 0.66f, Glass())
                .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            LowPoly.Part(ToyMeshes.Dome, "Cap", m, new Vector3(0f, 1.53f, 0f), new Vector3(0.32f, 0.2f, 0.32f), red);
            LowPoly.Part(ToyMeshes.Sphere, "CapKnob", m, new Vector3(0f, 1.64f, 0f), Vector3.one * 0.07f, chrome);
            var col = m.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.85f, 0f);
            col.size = new Vector3(0.62f, 1.7f, 0.62f);
        }

        /// <summary>Máquina de venda de bolas: corpo vermelho, vitrine com prateleiras e bolas atrás de vidro, tabuleiro de saída.</summary>
        void BuildBallVending(Transform root, Vector3 at)
        {
            var red = VisualFactory.Lit(ArtPalette.Red, 0.5f, 0.1f);
            var mustard = VisualFactory.Lit(ArtPalette.Mustard, 0.55f, 0.35f);
            var backing = VisualFactory.Emissive(new Color(0.1f, 0.13f, 0.24f), 0.35f);
            var dark = VisualFactory.Lit(ArtPalette.NavyDark, 0.5f);
            var m = LowPoly.Pivot("BallVending", root, at);
            m.localRotation = Quaternion.Euler(0f, 180f, 0f); // frente (+Z local) virada para o centro da sala
            LowPoly.Part(ToyMeshes.RoundedBox(1.1f, 2.1f, 0.7f, 0.07f), "Body", m, new Vector3(0f, 1.05f, 0f), Vector3.one, red);
            // Vitrine saliente: fundo iluminado, moldura, prateleiras, bolas e vidro à frente.
            const float zf = 0.35f;
            LowPoly.Part(ToyMeshes.RoundedBox(0.8f, 0.86f, 0.02f, 0.008f), "DisplayBack", m, new Vector3(0f, 1.42f, zf + 0.01f), Vector3.one, backing);
            LowPoly.Part(ToyMeshes.RoundedBox(0.92f, 0.07f, 0.2f, 0.02f), "FrameTop", m, new Vector3(0f, 1.88f, zf + 0.1f), Vector3.one, mustard);
            LowPoly.Part(ToyMeshes.RoundedBox(0.92f, 0.07f, 0.2f, 0.02f), "FrameBottom", m, new Vector3(0f, 0.96f, zf + 0.1f), Vector3.one, mustard);
            LowPoly.Part(ToyMeshes.RoundedBox(0.07f, 0.99f, 0.2f, 0.02f), "FrameLeft", m, new Vector3(-0.43f, 1.42f, zf + 0.1f), Vector3.one, mustard);
            LowPoly.Part(ToyMeshes.RoundedBox(0.07f, 0.99f, 0.2f, 0.02f), "FrameRight", m, new Vector3(0.43f, 1.42f, zf + 0.1f), Vector3.one, mustard);
            int n = 1;
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float y = 1.05f + shelf * 0.27f;
                LowPoly.Part(ToyMeshes.RoundedBox(0.8f, 0.02f, 0.16f, 0.006f), "Shelf", m, new Vector3(0f, y, zf + 0.09f), Vector3.one, dark);
                for (int i = 0; i < 3; i++)
                    LowPoly.Part(ToyMeshes.Sphere, "Ball", m, new Vector3(-0.24f + i * 0.24f, y + 0.1f, zf + 0.09f), Vector3.one * 0.17f, VisualFactory.ClearCoat(VisualFactory.BallTexture(n++)));
            }

            LowPoly.Part(ToyMeshes.RoundedBox(0.5f, 0.14f, 0.12f, 0.03f), "Tray", m, new Vector3(0f, 0.45f, zf + 0.05f), Vector3.one, dark);
            LowPoly.Part(ToyMeshes.RoundedBox(0.14f, 0.2f, 0.04f, 0.02f), "CoinPanel", m, new Vector3(0.36f, 0.75f, zf + 0.02f), Vector3.one, mustard);
            var label = VisualFactory.Text(m, "BOLAS", new Vector3(0f, 2.0f, zf + 0.02f), 0.18f, ArtPalette.Ivory);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var glow = new GameObject("VendingGlow").AddComponent<Light>();
            glow.transform.SetParent(m, false);
            glow.transform.localPosition = new Vector3(0f, 1.45f, zf + 0.5f);
            glow.type = LightType.Point;
            glow.color = new Color(0.55f, 0.65f, 1f);
            glow.intensity = 1.2f;
            glow.range = 2.5f;
            var col = m.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.05f, 0.05f);
            col.size = new Vector3(1.12f, 2.1f, 0.82f);
        }
        /// <summary>Bar no canto sudoeste: balcão Kenney, bancos, frigorífico, estante e garrafas pousadas no balcão.</summary>
        void BuildBar(Transform root)
        {
            float z = -HalfZ + 1.05f;
            float x0 = -6.4f;
            const float barHeight = 1.05f; // altura pedida ao PropLibrary = topo do balcão
            for (int i = 0; i < 3; i++)
                PropLibrary.Spawn(i == 2 ? "Furniture/kitchenBarEnd" : "Furniture/kitchenBar", root, new Vector3(x0 + i * 1.0f, 0f, z), 180f, barHeight);
            for (int i = 0; i < 4; i++)
                PropLibrary.Spawn(i % 2 == 0 ? "Furniture/stoolBar" : "Furniture/stoolBarSquare", root, new Vector3(x0 - 0.1f + i * 0.8f, 0f, z + 0.85f), 15f * i, 0.78f, colliderInset: 0.15f);
            PropLibrary.Spawn("Furniture/kitchenFridge", root, new Vector3(-7.05f, 0f, -HalfZ + 0.45f), 0f, 1.9f);
            PropLibrary.Spawn("Furniture/bookcaseOpen", root, new Vector3(-5.6f, 0f, -HalfZ + 0.3f), 0f, 1.8f);
            var rng = new System.Random(11);
            for (int i = 0; i < 9; i++)
            {
                string bottle = i % 3 == 0 ? "Survival/bottle-large" : "Survival/bottle";
                PropLibrary.Spawn(bottle, root, new Vector3(-6.1f + i * 0.13f, barHeight, z - 0.05f + (float)rng.NextDouble() * 0.1f), rng.Next(360), 0.26f, collider: false);
            }
            PropLibrary.Spawn("Furniture/radio", root, new Vector3(-4.6f, barHeight, z), 200f, 0.22f, collider: false);
            VisualFactory.Text(root, "BAR", new Vector3(-5.4f, 3.1f, -HalfZ + 0.05f), 0.6f, ArtPalette.Coral).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            AddLight(root, "BarNeon", new Vector3(-5.4f, 3.0f, -HalfZ + 0.6f), LightType.Point, ArtPalette.Coral, 2f, 3.5f, false);
            AddLight(root, "BarLight", new Vector3(-5.4f, 2.4f, z + 0.3f), LightType.Spot, new Color(1f, 0.7f, 0.45f), 5f, 4f, false, 120f);
        }

        /// <summary>Lounge a sul: sofá virado para a TV de parede, mesa de centro, tapete, poltrona, colunas, planta e candeeiro.</summary>
        void BuildLounge(Transform root)
        {
            float tvZ = -HalfZ;
            PropLibrary.Spawn("Furniture/rugRectangle", root, new Vector3(0.6f, 0.01f, -4.9f), 0f, 0.02f, 1f, false, false);
            // Sofá de frente para a televisão (os modelos Kenney olham para −Z com yaw 0).
            PropLibrary.Spawn("Furniture/loungeSofaLong", root, new Vector3(0.6f, 0f, -4.3f), 0f, 0.85f);
            PropLibrary.Spawn("Furniture/tableCoffee", root, new Vector3(0.6f, 0f, -5.25f), 0f, 0.42f);
            PropLibrary.Spawn("Furniture/plantSmall2", root, new Vector3(0.9f, 0.42f, -5.25f), 0f, 0.3f, collider: false);
            PropLibrary.Spawn("Furniture/loungeChair", root, new Vector3(-1.5f, 0f, -5.1f), 115f, 0.85f);
            PropLibrary.Spawn("Furniture/speaker", root, new Vector3(-0.85f, 0f, tvZ + 0.4f), 0f, 1.3f);
            PropLibrary.Spawn("Furniture/speaker", root, new Vector3(2.05f, 0f, tvZ + 0.4f), 0f, 1.3f);
            PropLibrary.Spawn("Furniture/pottedPlant", root, new Vector3(2.9f, 0f, tvZ + 0.5f), 30f, 1.2f, colliderInset: 0.2f);
            PropLibrary.Spawn("Furniture/lampRoundFloor", root, new Vector3(-2.2f, 0f, tvZ + 0.45f), 0f, 1.7f, colliderInset: 0.2f);
            AddLight(root, "LoungeLamp", new Vector3(-2.2f, 1.55f, tvZ + 0.6f), LightType.Point, new Color(1f, 0.8f, 0.55f), 2.2f, 4f, false);
            // Televisão de parede (moldura + ecrã emissivo) virada para a sala.
            LowPoly.Part(ToyMeshes.RoundedBox(1.7f, 1.0f, 0.08f, 0.03f), "TvFrame", root, new Vector3(0.6f, 2.2f, tvZ + 0.06f), Vector3.one, VisualFactory.Lit(new Color(0.06f, 0.06f, 0.08f), 0.6f));
            var screen = LowPoly.Part(ToyMeshes.RoundedBox(1.55f, 0.87f, 0.01f, 0.004f), "TvScreen", root, new Vector3(0.6f, 2.2f, tvZ + 0.105f), Vector3.one, VisualFactory.Emissive(new Color(0.16f, 0.45f, 0.42f), 1.3f));
            screen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var tv = VisualFactory.Text(root, "FRIEND SLOP TV", new Vector3(0.6f, 2.2f, tvZ + 0.115f), 0.14f, ArtPalette.Ivory);
            tv.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>Armazém a sudeste (contentores empilhados junto à parede, caixotes, barris) e oeste (barris, baú).</summary>
        void BuildStorage(Transform root)
        {
            // Contentores ao comprido junto à parede leste, dentro da sala (1,6 m de largura, 3,5 m de comprimento).
            PropLibrary.Spawn("Industrial/shipping-container-a", root, new Vector3(6.6f, 0f, -4.45f), 0f, 1.5f);
            PropLibrary.Spawn("Industrial/shipping-container-c", root, new Vector3(6.6f, 1.5f, -4.55f), 2f, 1.5f);
            var rng = new System.Random(9);
            foreach (var p in new[] { new Vector3(5.2f, 0f, -5.0f), new Vector3(4.7f, 0f, -4.6f), new Vector3(-6.8f, 0f, 4.4f), new Vector3(-6.7f, 0f, 0.3f) })
                PropLibrary.Spawn("Survival/barrel", root, p, rng.Next(360), 0.95f);
            foreach (var p in new[] { new Vector3(4.9f, 0f, -6.0f), new Vector3(4.15f, 0f, -6.05f) })
                PropLibrary.Spawn("Factory/box-large", root, p, rng.Next(-15, 15), 0.7f);
            PropLibrary.Spawn("Factory/box-small", root, new Vector3(4.5f, 0.7f, -6.0f), 12f, 0.45f);
            PropLibrary.Spawn("Survival/box-large", root, new Vector3(-6.9f, 0f, -3.4f), 5f, 0.6f);
            PropLibrary.Spawn("Survival/chest", root, new Vector3(-6.95f, 0f, 1.1f), 90f, 0.55f);
            PropLibrary.Spawn("Factory/screen-hanging-wide", root, new Vector3(0f, 4.0f, -HalfZ + 0.4f), 0f, 0.9f, collider: false);
        }
        /// <summary>Tubagens industriais ao longo das paredes, perto do tecto.</summary>
        void BuildPipes(Transform root)
        {
            for (int i = 0; i < 6; i++)
            {
                PropLibrary.Spawn("Factory/pipe-large-long", root, new Vector3(-HalfX + 0.25f, Height - 0.5f, -5.5f + i * 2.2f), 0f, 0.35f, collider: false);
                PropLibrary.Spawn("Factory/pipe-large-long", root, new Vector3(HalfX - 0.25f, Height - 0.5f, -5.5f + i * 2.2f), 0f, 0.35f, collider: false);
            }
            PropLibrary.Spawn("Factory/pipe-large-valve", root, new Vector3(-HalfX + 0.25f, 1.2f, -0.2f), 90f, 0.5f, collider: false);
        }
        static GameObject Solid(Transform root, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = VisualFactory.Primitive(PrimitiveType.Cube, name, root, position, scale, material);
            go.AddComponent<BoxCollider>();
            return go;
        }

        static Light AddLight(Transform root, string name, Vector3 position, LightType type, Color color, float intensity, float range, bool shadows, float spotAngle = 60f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            if (type == LightType.Spot) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle * 0.55f;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }
    }
}
