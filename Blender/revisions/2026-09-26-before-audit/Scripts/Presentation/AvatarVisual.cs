using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Presentation
{
    public enum AvatarPose
    {
        Idle,
        Aiming,
        Risk,
        Celebrate
    }

    /// <summary>
    /// Personagem do character sheet: boneco de vinil com 3 "cabeças" de altura (cabeça, tronco, pernas),
    /// tronco estreito azul-petróleo com suspensórios marfim, cinto e emblema △○□, luvas grandes e redondas,
    /// botas robustas com sola na cor do jogador, cabeça geométrica (triângulo, círculo ou quadrado) e boné.
    /// Malhas suaves (ToyMeshes) e esqueleto próprio animado por código — sem Animator.
    /// Só visual: não tem colliders competitivos (CHAR-002/003).
    /// </summary>
    public sealed partial class AvatarVisual : MonoBehaviour
    {
        public const float EyeHeight = 1.1f;
        const float HipHeight = 0.5f;
        const float HeadScale = 1.12f;

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Material> _materials = new List<Material>();
        readonly Dictionary<string, Material> _palette = new Dictionary<string, Material>();
        Color _tint;
        readonly Transform[] _stars = new Transform[3];

        Transform _rig, _hips, _spine, _neck, _head;
        Transform _shoulderL, _shoulderR, _elbowL, _elbowR, _handL, _handR;
        Transform _thighL, _thighR, _kneeL, _kneeR, _ankleL, _ankleR;
        Transform _starRing;
        PlayerProfile _built;
        bool _ghost;
        bool _hiddenFirstPerson;
        bool _popped;
        float _knockdown;
        float _popTimer = -1f;
        float _emoteTimer;
        int _emote = -1;
        float _idleOffset;

        public Transform Head => _head;
        public Transform RightHand => _handR;
        public Transform LeftHand => _handL;
        public bool Debuffed { get; set; }

        public static AvatarVisual Create(Transform parent, PlayerProfile profile)
        {
            var go = new GameObject("AvatarVisual");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<AvatarVisual>();
            v._idleOffset = Random.value * 10f; // público e bots não respiram todos ao mesmo tempo
            v.Build(profile);
            return v;
        }

        public void Apply(PlayerProfile profile)
        {
            if (_built != null && _built.Archetype == profile.Archetype && _built.Color == profile.Color &&
                _built.HatId == profile.HatId && _built.AccessoryId == profile.AccessoryId) return;
            Build(profile);
        }

        Material Mat(Color c, float smoothness = 0.35f, float metallic = 0f)
        {
            var m = VisualFactory.Lit(c, smoothness, metallic);
            _materials.Add(m);
            return m;
        }

        Transform P(Mesh mesh, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat) =>
            LowPoly.Part(mesh, name, parent, pos, scale, mat, _renderers).transform;

        Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, float radius, Material mat) =>
            P(ToyMeshes.RoundedBox(size.x, size.y, size.z, radius), name, parent, pos, Vector3.one, mat);

        Transform Limb(string name, Transform parent, float radius, float length, Material mat) =>
            P(ToyMeshes.Capsule(radius, length), name, parent, Vector3.zero, Vector3.one, mat);

        const string Model = "character";

        /// <summary>Peça modelada no Blender (Blender/character.py), se o ficheiro existir.</summary>
        bool Part(string name, Transform parent, Vector3 pos = default, float scale = 1f)
        {
            var m = FsModels.Get(Model, name);
            if (m == null) return false;
            FsModels.Spawn(m, parent, pos, Quaternion.identity, Vector3.one * scale, Resolve, _renderers);
            return true;
        }

        /// <summary>Chapéu/acessório do Blender (Blender/accessories.py) com as cores "hat" e "accent" desta peça.</summary>
        bool Accessory(string name, Transform parent, Vector3 pos, float scale, Color hat, Color accent) =>
            Accessory(name, parent, pos, Vector3.one * scale, hat, accent);

        bool Accessory(string name, Transform parent, Vector3 pos, Vector3 scale, Color hat, Color accent)
        {
            var m = FsModels.Get("accessories", name);
            if (m == null) return false;
            var hatMat = Mat(hat, 0.35f);
            var accentMat = Mat(accent, 0.45f);
            FsModels.Spawn(m, parent, pos, Quaternion.identity, scale,
                n => n == "hat" ? hatMat : n == "accent" ? accentMat : Resolve(n), _renderers);
            return true;
        }

        /// <summary>Chapéu encaixado pela geometria da cabeça (HatFit): nada da cabeça atravessa o chapéu.</summary>
        bool FittedHat(string name, HatFit.Shape shape, Transform headRoot, Archetype archetype, float top, float minY, Color hat, Color accent)
        {
            if (FsModels.Get("accessories", name) == null) return false;
            var fit = HatFit.Fit(archetype, shape, top, minY);
            return Accessory(name, headRoot, fit.Position, fit.Scale, hat, accent);
        }

        /// <summary>Nomes de material do Blender → paleta do character sheet ("tint" = cor do jogador).</summary>
        Material Resolve(string name)
        {
            if (_palette.TryGetValue(name, out var m)) return m;
            switch (name)
            {
                case "suit": m = Mat(ArtPalette.Hex(0x2A4566), 0.3f); break;
                case "suitDark": m = Mat(ArtPalette.NavyDark, 0.35f); break;
                case "ivory": m = Mat(ArtPalette.Ivory, 0.38f); break;
                case "ivoryDark": m = Mat(Color.Lerp(ArtPalette.Ivory, new Color(0.35f, 0.3f, 0.25f), 0.45f), 0.3f); break;
                case "leather": m = Mat(new Color(0.34f, 0.2f, 0.11f), 0.4f); break;
                case "brass": m = Mat(ArtPalette.Mustard, 0.8f, 0.7f); break;
                case "tint": m = Mat(Color.Lerp(_tint, Color.black, 0.08f), 0.45f); break;
                case "mustard": m = Mat(ArtPalette.Mustard, 0.5f); break;
                case "coral": m = Mat(ArtPalette.Coral, 0.5f); break;
                case "teal": m = Mat(ArtPalette.Teal, 0.5f); break;
                case "navy": m = Mat(ArtPalette.Navy, 0.35f); break;
                case "dark": m = Mat(new Color(0.03f, 0.035f, 0.05f), 0.5f); break;
                default: m = Mat(Color.gray, 0.4f); break;
            }
            _palette[name] = m;
            return m;
        }

        // ------------------------------------------------------------------ construção

        void Build(PlayerProfile profile)
        {
            if (_rig != null) Destroy(_rig.gameObject);
            _renderers.Clear();
            _materials.Clear();
            _normalMaterials.Clear();
            _ghostMaterials.Clear();
            _palette.Clear();
            _eyes.Clear();
            _built = profile.Clone();
            _tint = profile.Color;
            _popped = false;

            var tint = profile.Color;
            // Azul-petróleo do character sheet (mais claro que o navy da UI para não ficar preto na arena).
            var suit = Mat(ArtPalette.Hex(0x2A4566), 0.3f);
            var suitDark = Mat(ArtPalette.NavyDark, 0.3f);
            var ivory = Mat(ArtPalette.Ivory, 0.38f);
            var leather = Mat(new Color(0.32f, 0.2f, 0.12f), 0.35f);
            var brass = Mat(ArtPalette.Mustard, 0.8f, 0.7f);
            var sole = Mat(Color.Lerp(tint, Color.black, 0.08f), 0.45f);

            _rig = LowPoly.Pivot("Rig", transform, Vector3.zero);
            _hips = LowPoly.Pivot("Hips", _rig, new Vector3(0f, HipHeight, 0f));
            if (!Part("Pelvis", _hips)) Box("Pelvis", _hips, new Vector3(0f, -0.02f, 0f), new Vector3(0.28f, 0.15f, 0.21f), 0.06f, suit);

            BuildLeg(true, suit, suitDark, ivory, sole);
            BuildLeg(false, suit, suitDark, ivory, sole);

            // Tronco estreito e arredondado (1 unidade de cabeça de altura).
            _spine = LowPoly.Pivot("Spine", _hips, new Vector3(0f, 0.04f, 0f));
            // Tronco do Blender já traz suspensórios, cinto, bolsas e emblema.
            if (!Part("Torso", _spine))
            {
                Box("Torso", _spine, new Vector3(0f, 0.2f, 0f), new Vector3(0.32f, 0.4f, 0.22f), 0.09f, suit);
                BuildHarness(ivory, leather, brass, suitDark);
            }

            BuildArm(true, suit, ivory);
            BuildArm(false, suit, ivory);

            // Pescoço curto (gola marfim) e cabeça geométrica.
            _neck = LowPoly.Pivot("Neck", _spine, new Vector3(0f, 0.4f, 0f));
            if (!Part("Collar", _neck)) P(ToyMeshes.Capsule(0.075f, 0.07f), "Collar", _neck, new Vector3(0f, 0.045f, 0f), Vector3.one, ivory);
            _head = LowPoly.Pivot("Head", _neck, new Vector3(0f, 0.03f, 0f));
            BuildHead(profile);

            // Estrelas de tontura (debuff activo), escondidas por omissão.
            _starRing = LowPoly.Pivot("Stars", _head, new Vector3(0f, 0.68f, 0f));
            var star = Mat(ArtPalette.Mustard, 0.6f);
            for (int i = 0; i < _stars.Length; i++)
            {
                _stars[i] = P(ToyMeshes.Sphere, "Star", _starRing, Vector3.zero, Vector3.one * 0.07f, star);
                _stars[i].gameObject.SetActive(false);
            }

            ApplyRenderMode();
            if (_ghost) SetGhost(true, true);
        }

        void BuildLeg(bool left, Material suit, Material suitDark, Material ivory, Material sole)
        {
            float x = left ? -0.078f : 0.078f;
            var thigh = LowPoly.Pivot(left ? "ThighL" : "ThighR", _hips, new Vector3(x, -0.05f, 0f));
            if (!Part("Thigh", thigh)) Limb("ThighMesh", thigh, 0.066f, 0.21f, suit);
            var knee = LowPoly.Pivot("Knee", thigh, new Vector3(0f, -0.19f, 0f));
            if (!Part("Shin", knee)) Limb("ShinMesh", knee, 0.058f, 0.19f, suit);
            var ankle = LowPoly.Pivot("Ankle", knee, new Vector3(0f, -0.17f, 0f));
            // Bota robusta: cano marfim, corpo azul-escuro, biqueira marfim, sola grossa na cor do jogador.
            if (!Part("Boot", ankle))
            {
                P(ToyMeshes.Capsule(0.068f, 0.06f), "BootCuff", ankle, new Vector3(0f, 0.035f, 0f), Vector3.one, ivory);
                Box("Boot", ankle, new Vector3(0f, -0.025f, 0.035f), new Vector3(0.15f, 0.1f, 0.25f), 0.045f, suitDark);
                Box("BootToe", ankle, new Vector3(0f, -0.03f, 0.125f), new Vector3(0.14f, 0.075f, 0.07f), 0.03f, ivory);
                Box("Sole", ankle, new Vector3(0f, -0.075f, 0.035f), new Vector3(0.165f, 0.035f, 0.27f), 0.015f, sole);
            }
            if (left)
            {
                _thighL = thigh;
                _kneeL = knee;
                _ankleL = ankle;
            }
            else
            {
                _thighR = thigh;
                _kneeR = knee;
                _ankleR = ankle;
            }
        }

        void BuildArm(bool left, Material suit, Material ivory)
        {
            float x = left ? -0.2f : 0.2f;
            var shoulder = LowPoly.Pivot(left ? "ShoulderL" : "ShoulderR", _spine, new Vector3(x, 0.34f, 0f));
            if (!Part("UpperArm", shoulder))
            {
                P(ToyMeshes.Sphere, "ShoulderBall", shoulder, new Vector3(0f, -0.01f, 0f), Vector3.one * 0.12f, suit);
                Limb("UpperArm", shoulder, 0.052f, 0.17f, suit);
            }
            var elbow = LowPoly.Pivot("Elbow", shoulder, new Vector3(0f, -0.15f, 0f));
            if (!Part("Forearm", elbow))
            {
                Limb("Forearm", elbow, 0.047f, 0.14f, suit);
                P(ToyMeshes.Capsule(0.058f, 0.045f), "Cuff", elbow, new Vector3(0f, -0.1f, 0f), Vector3.one, ivory);
            }
            var hand = LowPoly.Pivot(left ? "HandL" : "HandR", elbow, new Vector3(0f, -0.16f, 0f));
            // Luva grande de desenho animado (punho almofadado, 3 dedos e polegar).
            if (!Part(left ? "GloveL" : "GloveR", hand))
            {
                P(ToyMeshes.Sphere, "Glove", hand, new Vector3(0f, -0.035f, 0f), new Vector3(0.15f, 0.14f, 0.14f), ivory);
                P(ToyMeshes.Sphere, "Thumb", hand, new Vector3(left ? 0.06f : -0.06f, -0.005f, 0.04f), new Vector3(0.06f, 0.075f, 0.06f), ivory);
            }
            if (left)
            {
                _shoulderL = shoulder;
                _elbowL = elbow;
                _handL = hand;
            }
            else
            {
                _shoulderR = shoulder;
                _elbowR = elbow;
                _handR = hand;
            }
        }

        /// <summary>Suspensórios marfim, cinto com fivela, bolsa e emblema △○□ (equipment areas do concept).</summary>
        void BuildHarness(Material ivory, Material leather, Material brass, Material dark)
        {
            Box("Belt", _spine, new Vector3(0f, 0.04f, 0f), new Vector3(0.345f, 0.06f, 0.245f), 0.03f, leather);
            Box("Buckle", _spine, new Vector3(0f, 0.04f, 0.124f), new Vector3(0.06f, 0.05f, 0.016f), 0.008f, brass);
            Box("Pouch", _spine, new Vector3(0.14f, 0.02f, 0.09f), new Vector3(0.075f, 0.075f, 0.06f), 0.02f, leather);
            Box("PouchFlap", _spine, new Vector3(0.14f, 0.05f, 0.121f), new Vector3(0.078f, 0.025f, 0.012f), 0.006f, brass);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.078f;
                Box("StrapFront", _spine, new Vector3(x, 0.235f, 0.112f), new Vector3(0.045f, 0.33f, 0.014f), 0.006f, ivory);
                Box("StrapBack", _spine, new Vector3(x, 0.235f, -0.112f), new Vector3(0.045f, 0.33f, 0.014f), 0.006f, ivory);
                Box("StrapTop", _spine, new Vector3(x, 0.4f, 0f), new Vector3(0.045f, 0.014f, 0.235f), 0.006f, ivory);
                Box("StrapClip", _spine, new Vector3(x, 0.33f, 0.121f), new Vector3(0.05f, 0.022f, 0.012f), 0.005f, brass);
            }
            // Emblema do peito: disco escuro com aro dourado e △○□.
            var badge = LowPoly.Pivot("Badge", _spine, new Vector3(0f, 0.23f, 0.116f));
            P(ToyMeshes.Sphere, "BadgeRim", badge, Vector3.zero, new Vector3(0.1f, 0.1f, 0.022f), brass);
            P(ToyMeshes.Sphere, "BadgeDisc", badge, new Vector3(0f, 0f, 0.004f), new Vector3(0.082f, 0.082f, 0.02f), dark);
            P(VisualFactory.TriangularPrismShared, "BadgeTri", badge, new Vector3(-0.022f, 0.006f, 0.013f), new Vector3(0.06f, 0.055f, 0.01f), Mat(ArtPalette.Mustard, 0.5f));
            P(ToyMeshes.Sphere, "BadgeCircle", badge, new Vector3(0f, -0.015f, 0.013f), new Vector3(0.022f, 0.022f, 0.008f), Mat(ArtPalette.Coral, 0.5f));
            Box("BadgeSquare", badge, new Vector3(0.022f, 0.006f, 0.013f), new Vector3(0.02f, 0.02f, 0.008f), 0.004f, Mat(ArtPalette.Teal, 0.5f));
        }

        void BuildHead(PlayerProfile profile)
        {
            var headColor = profile.Color;
            // Vinil brilhante como nos concepts (brinquedo), com um pouco de verniz.
            var head = Mat(headColor, 0.62f);
            var navy = Mat(ArtPalette.Navy, 0.35f);
            var navyDark = Mat(ArtPalette.NavyDark, 0.4f);
            var ivory = Mat(ArtPalette.Ivory, 0.5f);
            var leather = Mat(ArtPalette.Leather, 0.3f);
            var brass = Mat(ArtPalette.Mustard, 0.75f, 0.6f);
            var eye = Mat(new Color(0.02f, 0.02f, 0.035f), 0.97f);
            var shine = Mat(Color.white, 0.9f);

            var shape = LowPoly.Pivot("Shape", _head, Vector3.zero);
            shape.localScale = Vector3.one * HeadScale;
            float headTop, faceZ, eyeY, eyeX;
            bool round = false;
            switch (profile.Archetype)
            {
                case Archetype.Triangle:
                    P(ToyMeshes.TriangleHead, "HeadShape", shape, Vector3.zero, Vector3.one, head);
                    headTop = 0.53f;
                    faceZ = 0.2f;
                    eyeY = 0.16f;
                    eyeX = 0.072f;
                    break;
                case Archetype.Square:
                    P(ToyMeshes.SquareHead, "HeadShape", shape, Vector3.zero, Vector3.one, head);
                    headTop = 0.43f;
                    faceZ = 0.2f;
                    eyeY = 0.2f;
                    eyeX = 0.085f;
                    break;
                default:
                    P(ToyMeshes.Sphere, "HeadShape", shape, new Vector3(0f, 0.235f, 0f), Vector3.one * 0.47f, head);
                    headTop = 0.47f;
                    faceZ = 0.222f;
                    eyeY = 0.24f;
                    eyeX = 0.075f;
                    round = true;
                    break;
            }

            // Olhos: ovais pretos brilhantes com um reflexo branco (concept 01).
            foreach (float side in new[] { -1f, 1f })
            {
                var eyePivot = LowPoly.Pivot("EyePivot", shape, new Vector3(side * eyeX, eyeY, faceZ));
                _eyes.Add(eyePivot);
                if (round) eyePivot.localRotation = Quaternion.Euler(0f, side * Mathf.Asin(eyeX / 0.235f) * Mathf.Rad2Deg, 0f);
                P(ToyMeshes.Sphere, "Eye", eyePivot, new Vector3(0f, 0f, 0.002f), new Vector3(0.056f, 0.1f, 0.03f), eye);
                P(ToyMeshes.Sphere, "EyeShine", eyePivot, new Vector3(-0.011f, 0.024f, 0.014f), new Vector3(0.017f, 0.022f, 0.008f), shine);
            }

            // A base dos chapéus nunca desce abaixo dos olhos.
            BuildHat(shape, profile.HatId, headTop, eyeY + 0.07f, profile.Archetype, navy, ivory, brass, headColor);
            BuildAccessory(shape, profile.AccessoryId, eyeY, faceZ, navyDark, leather, brass, headColor);
        }

        void BuildHat(Transform headRoot, int id, float top, float minY, Archetype archetype, Material navy, Material ivory, Material brass, Color tint)
        {
            bool tri = archetype == Archetype.Triangle;
            switch (id)
            {
                case 0: // boné da casa (character sheet): cúpula azul, aba marfim, emblema △○□
                {
                    float tilt = tri ? -8f : -6f;
                    var capFit = HatFit.Fit(archetype, HatFit.Cap, top, minY, tilt, tri ? 0.005f : 0f);
                    var cap = LowPoly.Pivot("Cap", headRoot, capFit.Position);
                    cap.localRotation = Quaternion.Euler(tilt, 0f, 0f);
                    cap.localScale = capFit.Scale;
                    if (Part("Cap", cap)) break;
                    P(ToyMeshes.Dome, "CapDome", cap, Vector3.zero, new Vector3(0.47f, 0.36f, 0.47f), navy);
                    P(ToyMeshes.Sphere, "CapButton", cap, new Vector3(0f, 0.175f, 0f), Vector3.one * 0.035f, navy);
                    P(ToyMeshes.Sphere, "CapBrim", cap, new Vector3(0f, 0.012f, 0.2f), new Vector3(0.34f, 0.028f, 0.24f), ivory);
                    var logo = LowPoly.Pivot("CapLogo", cap, new Vector3(0f, 0.085f, 0.2f));
                    logo.localRotation = Quaternion.Euler(-38f, 0f, 0f);
                    P(ToyMeshes.Sphere, "LogoDisc", logo, Vector3.zero, new Vector3(0.09f, 0.09f, 0.02f), ivory);
                    P(VisualFactory.TriangularPrismShared, "LogoTri", logo, new Vector3(-0.022f, 0.002f, 0.01f), new Vector3(0.055f, 0.05f, 0.01f), Mat(ArtPalette.Mustard, 0.5f));
                    P(ToyMeshes.Sphere, "LogoCircle", logo, new Vector3(0f, 0.022f, 0.01f), new Vector3(0.022f, 0.022f, 0.008f), Mat(ArtPalette.Coral, 0.5f));
                    P(LowPoly.BevelBox(0.2f), "LogoSquare", logo, new Vector3(0.022f, -0.004f, 0.01f), new Vector3(0.02f, 0.02f, 0.008f), Mat(ArtPalette.Teal, 0.5f));
                    break;
                }
                case 2 when FittedHat("TopHat", HatFit.TopHat, headRoot, archetype, top, minY, ArtPalette.NavyDark, tint):
                case 3 when FittedHat("PartyHat", HatFit.PartyHat, headRoot, archetype, top, minY, tint,
                    Color.HSVToRGB(Mathf.Repeat(Hue(tint) + 0.5f, 1f), 0.65f, 0.95f)):
                case 4 when FittedHat("Crown", HatFit.Crown, headRoot, archetype, top, minY, ArtPalette.Mustard, tint):
                case 5 when FittedHat("Beanie", HatFit.Beanie, headRoot, archetype, top, minY,
                    Color.Lerp(tint, Color.black, 0.45f), Color.Lerp(tint, Color.white, 0.25f)):
                    break;
                case 2: // cartola
                    P(LowPoly.Limb(20, 0.25f, 0.25f, 0.015f), "HatBrim", headRoot, new Vector3(0f, top, 0f), Vector3.one, navy);
                    P(LowPoly.Limb(20, 0.16f, 0.16f, 0.26f), "HatTop", headRoot, new Vector3(0f, top + 0.27f, 0f), Vector3.one, navy);
                    P(LowPoly.Limb(20, 0.165f, 0.165f, 0.04f), "HatBand", headRoot, new Vector3(0f, top + 0.05f, 0f), Vector3.one, Mat(tint, 0.5f));
                    break;
                case 3: // chapéu de festa
                    LowPoly.Part(VisualFactory.ConeShared, "PartyHat", headRoot, new Vector3(0f, top - 0.03f, 0f), new Vector3(0.14f, 0.34f, 0.14f),
                        Mat(Color.HSVToRGB(Mathf.Repeat(Hue(tint) + 0.5f, 1f), 0.7f, 1f), 0.4f), _renderers);
                    P(ToyMeshes.Sphere, "PartyPom", headRoot, new Vector3(0f, top + 0.33f, 0f), Vector3.one * 0.07f, ivory);
                    break;
                case 4: // coroa
                    P(LowPoly.Limb(16, 0.17f, 0.17f, 0.06f), "CrownBase", headRoot, new Vector3(0f, top + 0.04f, 0f), Vector3.one, brass);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f;
                        P(ToyMeshes.Sphere, "CrownSpike", headRoot, new Vector3(Mathf.Cos(a) * 0.15f, top + 0.1f, Mathf.Sin(a) * 0.15f), new Vector3(0.06f, 0.12f, 0.06f), brass);
                    }
                    break;
                case 5: // gorro
                    P(ToyMeshes.Dome, "Beanie", headRoot, new Vector3(0f, top - 0.1f, 0f), new Vector3(0.46f, 0.42f, 0.46f), Mat(Color.Lerp(tint, Color.black, 0.4f), 0.1f));
                    P(ToyMeshes.Sphere, "Pompom", headRoot, new Vector3(0f, top + 0.12f, 0f), Vector3.one * 0.11f, ivory);
                    break;
            }
        }

        void BuildAccessory(Transform headRoot, int id, float eyeY, float faceZ, Material dark, Material leather, Material brass, Color tint)
        {
            var accent = Color.HSVToRGB(Mathf.Repeat(Hue(tint) + 0.33f, 1f), 0.75f, 0.9f);
            switch (id)
            {
                case 2 when Accessory("Bow", _neck, Vector3.zero, 1f, accent, accent):
                case 3 when Accessory("Medal", _spine, new Vector3(0f, 0f, 0.014f), 1f, tint, accent):
                case 4 when Accessory("Scarf", _neck, Vector3.zero, 1f, Color.Lerp(tint, Color.white, 0.45f), Color.Lerp(tint, Color.white, 0.45f)):
                case 5 when Accessory("Backpack", _spine, Vector3.zero, 1f, ArtPalette.NavyDark, tint):
                    break;
                case 1: // óculos
                    foreach (float side in new[] { -0.075f, 0.075f })
                        P(LowPoly.Ring(16, 0.05f, 0.012f, 0.012f), "Lens", headRoot, new Vector3(side, eyeY, faceZ + 0.03f), Vector3.one, dark);
                    P(LowPoly.BevelBox(), "Bridge", headRoot, new Vector3(0f, eyeY, faceZ + 0.03f), new Vector3(0.05f, 0.01f, 0.01f), dark);
                    break;
                case 2: // laço
                    var bow = Mat(Color.HSVToRGB(Mathf.Repeat(Hue(tint) + 0.33f, 1f), 0.8f, 0.9f), 0.45f);
                    foreach (float side in new[] { -0.06f, 0.06f })
                        P(ToyMeshes.Sphere, "Bow", _neck, new Vector3(side, -0.03f, 0.125f), new Vector3(0.09f, 0.065f, 0.035f), bow)
                            .localRotation = Quaternion.Euler(0f, 0f, side > 0 ? 20f : -20f);
                    P(ToyMeshes.Sphere, "BowKnot", _neck, new Vector3(0f, -0.03f, 0.135f), new Vector3(0.04f, 0.045f, 0.04f), bow);
                    break;
                case 3: // medalha (antes era um bigode, que parecia uma boca)
                    P(ToyMeshes.Sphere, "Medal", _spine, new Vector3(0f, 0.225f, 0.15f), new Vector3(0.07f, 0.07f, 0.015f), brass);
                    break;
                case 4: // cachecol
                    var scarf = Mat(Color.Lerp(tint, Color.white, 0.45f), 0.1f);
                    P(LowPoly.Limb(16, 0.13f, 0.13f, 0.07f), "Scarf", _neck, new Vector3(0f, 0.05f, 0f), Vector3.one, scarf);
                    P(LowPoly.BevelBox(), "ScarfTail", _spine, new Vector3(0.07f, 0.27f, 0.125f), new Vector3(0.08f, 0.2f, 0.025f), scarf);
                    break;
                case 5: // mochila
                    P(LowPoly.BevelBox(0.25f), "Backpack", _spine, new Vector3(0f, 0.22f, -0.19f), new Vector3(0.3f, 0.32f, 0.14f), dark);
                    P(LowPoly.BevelBox(0.25f), "BackpackPocket", _spine, new Vector3(0f, 0.14f, -0.275f), new Vector3(0.24f, 0.15f, 0.05f), leather);
                    P(LowPoly.BevelBox(), "BackpackClip", _spine, new Vector3(0f, 0.23f, -0.265f), new Vector3(0.05f, 0.03f, 0.02f), brass);
                    break;
            }
        }
        static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        // ------------------------------------------------------------------ modos

        /// <summary>O jogador local não vê o próprio corpo, mas a sombra continua (primeira pessoa).</summary>
        public void SetFirstPersonHidden(bool hidden)
        {
            if (_hiddenFirstPerson == hidden) return;
            _hiddenFirstPerson = hidden;
            ApplyRenderMode();
        }

        void ApplyRenderMode()
        {
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                // Fantasma: sem sombra; o próprio fantasma em primeira pessoa não se vê (senão a câmara fica dentro do corpo).
                r.enabled = !(_ghost && _hiddenFirstPerson);
                r.shadowCastingMode = _ghost ? ShadowCastingMode.Off : _hiddenFirstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
        }

        /// <summary>SPEC-001: eliminados viram fantasmas translúcidos.</summary>
        static Shader s_ghostShader;
        readonly Dictionary<Renderer, Material[]> _normalMaterials = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Material> _ghostMaterials = new Dictionary<Material, Material>();

        /// <summary>SPEC-001: eliminados viram fantasmas translúcidos com contorno brilhante (shader FriendSlop/Ghost).</summary>
        public void SetGhost(bool ghost, bool force = false)
        {
            if (_ghost == ghost && !force) return;
            _ghost = ghost;
            if (!ghost) _popped = false;
            if (s_ghostShader == null) s_ghostShader = Resources.Load<Shader>("Shaders/Ghost");
            if (s_ghostShader == null)
            {
                foreach (var m in _materials)
                {
                    VisualFactory.SetTransparent(m, ghost);
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    c.a = ghost ? 0.32f : 1f;
                    VisualFactory.SetColor(m, c);
                }
                ApplyRenderMode();
                return;
            }
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                if (ghost)
                {
                    if (!_normalMaterials.ContainsKey(r)) _normalMaterials[r] = r.sharedMaterials;
                    var normal = _normalMaterials[r];
                    var swapped = new Material[normal.Length];
                    for (int i = 0; i < normal.Length; i++) swapped[i] = GhostOf(normal[i]);
                    r.sharedMaterials = swapped;
                }
                else if (_normalMaterials.TryGetValue(r, out var normal))
                {
                    r.sharedMaterials = normal;
                }
            }
            if (!ghost) _normalMaterials.Clear();
            ApplyRenderMode();
        }

        Material GhostOf(Material m)
        {
            if (m == null) return null;
            if (_ghostMaterials.TryGetValue(m, out var g)) return g;
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            // Cores lavadas na direcção da cor do jogador, meio transparentes.
            var tint = Color.Lerp(Color.Lerp(c, _tint, 0.45f), new Color(0.75f, 0.9f, 1f), 0.25f);
            tint.a = 0.36f;
            g = new Material(s_ghostShader) { name = "Ghost_" + m.name };
            g.SetColor("_Color", tint);
            g.SetColor("_RimColor", Color.Lerp(new Color(0.7f, 0.95f, 1f), _tint, 0.3f));
            _ghostMaterials[m] = g;
            _materials.Add(g);
            return g;
        }

        public void Knockdown() => _knockdown = 1.6f;

        /// <summary>Eliminação: o personagem "rebenta" em gosma (sem gore) e reaparece como fantasma.</summary>
        public void Pop() => _popTimer = 0f;

        public void PlayEmote(int emote)
        {
            _emote = emote;
            _emoteTimer = 2.4f;
        }

        /// <summary>Volta a mostrar o corpo (nova partida / revanche).</summary>
        public void ResetState()
        {
            _popped = false;
            _popTimer = -1f;
            _knockdown = 0f;
            _rig.localScale = Vector3.one;
        }
    }
}
