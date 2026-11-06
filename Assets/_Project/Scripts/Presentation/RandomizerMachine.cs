using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Máquina de sorteio (concepts 10/13/28). O rolo mostra quem vai à cadeira de risco e a roda mostra o
    /// resultado do gatilho. É só apresentação (UI-007): o rolo termina sempre no SelectedPlayerId que o
    /// servidor já decidiu e a roda pára no segmento do resultado real. Frente em −Z local.
    /// </summary>
    public sealed class RandomizerMachine : MonoBehaviour
    {
        const float WheelRadius = 0.72f;

        sealed class ReelPanel
        {
            public Transform Content;
            public Transform[] Shapes;
            public Material ShapeMaterial;
            public TextMesh Name;
            public Material Frame;
        }

        readonly ReelPanel[] _panels = new ReelPanel[3];
        readonly List<Renderer> _bulbs = new List<Renderer>();
        Material _bulbOn;
        Material _bulbOff;
        Material _centerFrameOn;
        Material _centerFrameOff;
        Transform _wheel;
        Transform _lever;
        int _segments;
        IReadOnlyList<ModifierDefinition> _catalog;
        Material _gooMaterial;
        Material _dividerMaterial;
        readonly List<GameObject> _segmentObjects = new List<GameObject>();
        readonly List<TextMesh> _segmentLabels = new List<TextMesh>();
        readonly List<Renderer> _segmentRenderers = new List<Renderer>();
        readonly List<bool> _segmentLoaded = new List<bool>();
        int _builtChambers = -1, _builtLoaded = -1, _builtRound = -1;
        float _wheelAngle;
        float _wheelFrom;
        float _wheelTo;
        float _wheelTime = -1f;
        int _lastCenter = -2;
        float _bounce;
        float _leverPull;
        GameBootstrap _game;

        /// <summary>Revólver deitado na bandeja (de lado, cano para a direita): encosta na bandeja, sem a atravessar.</summary>
        public Pose RestPose => new Pose(transform.TransformPoint(new Vector3(0f, 0.702f, -0.61f)), transform.rotation * Quaternion.Euler(0f, 90f, 90f));

        public void Build(GameBootstrap game, IReadOnlyList<ModifierDefinition> catalog)
        {
            _game = game;
            var navy = VisualFactory.Lit(ArtPalette.Hex(0x2A4566), 0.4f, 0.25f);
            var navyDark = VisualFactory.Lit(ArtPalette.NavyDark, 0.4f);
            var mustard = VisualFactory.Lit(ArtPalette.Mustard, 0.55f, 0.4f);
            var ivory = VisualFactory.Lit(ArtPalette.Ivory, 0.4f);
            var red = VisualFactory.Lit(ArtPalette.Red, 0.85f);
            var box = LowPoly.BevelBox(0.06f);
            var root = transform;

            // Corpo modelado no Blender (Blender/randomizer.py): base em degraus, pilares, canos e leque.
            var bodyModel = FsModels.Get("randomizer", "RandomizerBody");
            if (bodyModel != null)
            {
                var cache = new Dictionary<string, Material>
                {
                    { "navy", VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(1f, 1f), 0.4f, 0.3f, 0.7f, new Color(0.8f, 0.85f, 0.95f)) },
                    { "railMustard", VisualFactory.Textured(TextureFactory.WornMustard(), new Vector2(3f, 3f), 0.55f, 0.35f, 1f) },
                    { "mustard", mustard }, { "steel", VisualFactory.Lit(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.9f) }, { "ivory", ivory },
                    { "dark", VisualFactory.Lit(new Color(0.02f, 0.025f, 0.035f), 0.3f) }
                };
                FsModels.Spawn(bodyModel, root, Vector3.zero, Quaternion.identity, Vector3.one, n => cache.TryGetValue(n, out var m) ? m : navy);
            }
            else
            {
                LowPoly.Part(box, "Plinth", root, new Vector3(0f, 0.3f, -0.1f), new Vector3(2.7f, 0.6f, 1.25f), mustard);
                LowPoly.Part(box, "PlinthTop", root, new Vector3(0f, 0.62f, -0.1f), new Vector3(2.5f, 0.06f, 1.1f), navyDark);
                LowPoly.Part(box, "Body", root, new Vector3(0f, 2.0f, 0f), new Vector3(2.2f, 2.8f, 0.9f), navy);
                foreach (float x in new[] { -1.12f, 1.12f })
                    LowPoly.Part(box, "SideTrim", root, new Vector3(x, 2.0f, 0f), new Vector3(0.1f, 2.9f, 0.96f), mustard);
                LowPoly.Part(box, "TopCap", root, new Vector3(0f, 3.45f, 0f), new Vector3(2.4f, 0.12f, 1f), mustard);
            }

            // Placa superior.
            LowPoly.Part(box, "Marquee", root, new Vector3(0f, 3.78f, -0.05f), new Vector3(2.5f, 0.5f, 0.2f), navyDark);
            LowPoly.Part(box, "MarqueeFace", root, new Vector3(0f, 3.78f, -0.16f), new Vector3(2.36f, 0.4f, 0.02f), mustard);
            VisualFactory.Text(root, "QUEM PUXA O GATILHO?", new Vector3(0f, 3.78f, -0.18f), 0.26f, ArtPalette.NavyDark);
            var eight = LowPoly.Part(ToyMeshes.Sphere, "Top8", root, new Vector3(0f, 4.2f, -0.05f), Vector3.one * 0.34f, VisualFactory.Lit(Color.white, 0.9f));
            VisualFactory.SetTexture(eight.GetComponent<Renderer>().sharedMaterial, VisualFactory.BallTexture(8));

            // Rolo de jogadores.
            LowPoly.Part(box, "ReelFrame", root, new Vector3(0f, 2.78f, -0.46f), new Vector3(2.0f, 0.86f, 0.06f), mustard);
            _centerFrameOn = VisualFactory.Emissive(ArtPalette.Mustard, 4f);
            _centerFrameOff = mustard;
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.64f;
                var frame = LowPoly.Part(box, "PanelFrame", root, new Vector3(x, 2.78f, -0.5f), new Vector3(0.6f, 0.74f, 0.03f), i == 1 ? _centerFrameOn : navyDark);
                var face = LowPoly.Part(box, "PanelFace", root, new Vector3(x, 2.78f, -0.52f), new Vector3(0.52f, 0.66f, 0.02f), ivory);
                var content = LowPoly.Pivot("Content", root, new Vector3(x, 2.8f, -0.55f));
                var shapeMat = VisualFactory.Lit(Color.white, 0.5f);
                var shapes = new[]
                {
                    LowPoly.Part(VisualFactory.TriangularPrismShared, "Tri", content, new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.85f, 0.4f), shapeMat).transform,
                    // Achatada como as outras formas do cartão: os olhos (z = −0,09) ficam à frente da superfície.
                    LowPoly.Part(ToyMeshes.Sphere, "Circle", content, new Vector3(0f, 0.06f, 0f), new Vector3(0.3f, 0.3f, 0.16f), shapeMat).transform,
                    LowPoly.Part(LowPoly.BevelBox(0.15f), "Square", content, new Vector3(0f, 0.06f, 0f), new Vector3(0.28f, 0.26f, 0.12f), shapeMat).transform,
                };
                var eyeMat = VisualFactory.Lit(Color.black, 0.9f);
                foreach (float ex in new[] { -0.045f, 0.045f })
                    LowPoly.Part(LowPoly.Icosphere(1), "Eye", content, new Vector3(ex, 0.07f, -0.09f), new Vector3(0.03f, 0.055f, 0.02f), eyeMat);
                var name = VisualFactory.Text(content, "", new Vector3(0f, -0.22f, -0.02f), 0.09f, ArtPalette.NavyDark);
                _panels[i] = new ReelPanel { Content = content, Shapes = shapes, ShapeMaterial = shapeMat, Name = name, Frame = frame.GetComponent<Renderer>().sharedMaterial };
                face.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Roda de punição.
            // Fundo da roda: disco com a face da frente 2 cm atrás das fatias (antes 1 cm) e sem receber a própria
            // sombra da roda a girar (era a "base" que piscava de perto).
            var wheelBack = LowPoly.Part(LowPoly.Limb(48, WheelRadius + 0.12f, WheelRadius + 0.12f, 0.07f), "WheelBack", root, new Vector3(0f, 1.3f, -0.45f), Vector3.one, navyDark);
            wheelBack.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            wheelBack.transform.localPosition = new Vector3(0f, 1.3f, -0.45f);
            var backRenderer = wheelBack.GetComponent<Renderer>();
            backRenderer.receiveShadows = false;
            _wheel = LowPoly.Pivot("Wheel", root, new Vector3(0f, 1.3f, -0.54f));
            _catalog = catalog;
            _gooMaterial = VisualFactory.Lit(ArtPalette.Goo, 0.6f);
            _dividerMaterial = VisualFactory.Lit(ArtPalette.Mustard, 0.25f);
            BuildChambers(6, 1, 1);
            var hub = LowPoly.Part(ToyMeshes.Sphere, "Hub", _wheel, new Vector3(0f, 0f, -0.03f), new Vector3(0.24f, 0.24f, 0.08f), VisualFactory.Lit(Color.white, 0.9f));
            VisualFactory.SetTexture(hub.GetComponent<Renderer>().sharedMaterial, VisualFactory.BallTexture(8));
            var pointer = LowPoly.Part(VisualFactory.TriangularPrismShared, "Pointer", root, new Vector3(0f, 1.3f + WheelRadius + 0.1f, -0.58f), new Vector3(0.5f, -0.45f, 0.2f), red);
            pointer.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _bulbOn = VisualFactory.Emissive(new Color(1f, 0.82f, 0.45f), 5f);
            _bulbOff = VisualFactory.Lit(new Color(0.35f, 0.3f, 0.2f), 0.8f);
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                var b = LowPoly.Part(LowPoly.Icosphere(1), "Bulb", root, new Vector3(Mathf.Cos(a) * (WheelRadius + 0.07f), 1.3f + Mathf.Sin(a) * (WheelRadius + 0.07f), -0.56f), Vector3.one * 0.06f, _bulbOn);
                _bulbs.Add(b.GetComponent<Renderer>());
            }
            for (int i = 0; i < 12; i++)
            {
                var b = LowPoly.Part(LowPoly.Icosphere(1), "MarqueeBulb", root, new Vector3(-1.1f + i * 0.2f, 3.5f, -0.5f), Vector3.one * 0.07f, _bulbOn);
                _bulbs.Add(b.GetComponent<Renderer>());
            }

            // Coluna de fichas (sólida): discos nas cores dos jogadores empilhados num suporte mostarda.
            LowPoly.Part(ToyMeshes.RoundedBox(0.34f, 0.08f, 0.34f, 0.03f), "TokenBase", root, new Vector3(-1.45f, 1.0f, -0.1f), Vector3.one, mustard);
            LowPoly.Part(ToyMeshes.Capsule(0.03f, 1.55f), "TokenPole", root, new Vector3(-1.45f, 2.6f, -0.1f), Vector3.one, mustard);
            // Braçadeiras a prender a coluna de fichas ao pilar da máquina (antes flutuava ao lado).
            foreach (float by in new[] { 1.02f, 2.3f })
                LowPoly.Part(box, "TokenBracket", root, new Vector3(-1.31f, by, -0.1f), new Vector3(0.3f, 0.05f, 0.07f), mustard);
            for (int i = 0; i < 7; i++)
                LowPoly.Part(ToyMeshes.RoundedBox(0.3f, 0.07f, 0.3f, 0.03f), "Token", root, new Vector3(-1.45f, 1.1f + i * 0.08f, -0.1f), Vector3.one,
                    VisualFactory.Lit(ArtPalette.Heads[i % ArtPalette.Heads.Length], 0.6f));
            // Alavanca.
            LowPoly.Part(box, "LeverBase", root, new Vector3(1.3f, 1.5f, -0.1f), new Vector3(0.16f, 0.4f, 0.3f), mustard);
            _lever = LowPoly.Pivot("Lever", root, new Vector3(1.4f, 1.5f, -0.1f));
            LowPoly.Part(LowPoly.Limb(6, 0.03f, 0.03f, 0.6f), "LeverArm", _lever, Vector3.zero, Vector3.one, VisualFactory.Lit(new Color(0.7f, 0.7f, 0.75f), 0.8f, 0.9f))
                .transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            LowPoly.Part(LowPoly.Icosphere(1), "LeverKnob", _lever, new Vector3(0f, 0.62f, 0f), Vector3.one * 0.14f, red);

            // Botão e aro assentes no tampo do degrau (0,62 m): antes o aro flutuava 1 cm acima.
            LowPoly.Part(LowPoly.Icosphere(1), "StartButton", root, new Vector3(0.7f, 0.668f, -0.55f), new Vector3(0.22f, 0.1f, 0.22f), red);
            LowPoly.Part(LowPoly.Limb(24, 0.15f, 0.15f, 0.04f), "StartRing", root, new Vector3(0.7f, 0.662f, -0.55f), Vector3.one, mustard);
            // Bandeja pousada no degrau (antes flutuava 3 cm acima do tampo, com a face de baixo à vista).
            LowPoly.Part(box, "GunTray", root, new Vector3(0f, 0.648f, -0.62f), new Vector3(0.62f, 0.05f, 0.3f), VisualFactory.Lit(new Color(0.45f, 0.1f, 0.12f), 0.1f));

            // Colisão da frente do degrau (−0,85) até à parede de trás (+1,25): antes sobrava um corredor atrás da
            // máquina por onde se passava através do modelo. Cobre também a coluna de fichas à esquerda.
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(-0.075f, 2f, 0.2f);
            col.size = new Vector3(3.15f, 4f, 2.1f);
        }

        /// <summary>
        /// A roda é o tambor do revólver: uma fatia por tambor, as carregadas a verde-gosma. As balas ficam em
        /// posições que mudam de ronda para ronda; as fatias de debuff mostram "?" até sair o efeito.
        /// </summary>
        void BuildChambers(int chambers, int loaded, int round)
        {
            chambers = Mathf.Clamp(chambers, 2, 12);
            loaded = Mathf.Clamp(loaded, 0, chambers);
            if (chambers == _builtChambers && loaded == _builtLoaded && round == _builtRound) return;
            _builtChambers = chambers;
            _builtLoaded = loaded;
            _builtRound = round;
            foreach (var go in _segmentObjects) Destroy(go);
            _segmentObjects.Clear();
            _segmentLabels.Clear();
            _segmentRenderers.Clear();
            _segmentLoaded.Clear();
            _segments = chambers;

            var order = new List<int>();
            for (int i = 0; i < chambers; i++) order.Add(i);
            var rng = new System.Random(round * 7919 + chambers * 31 + loaded);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int i = 0; i < chambers; i++)
            {
                bool goo = order[i] < loaded;
                _segmentLoaded.Add(goo);
                // Fatias de debuff em cores bem diferentes do verde da gosma.
                Color[] debuffColors = { ArtPalette.Mustard, ArtPalette.Coral, ArtPalette.Violet, ArtPalette.Ivory, ArtPalette.Teal };
                Color color = goo ? ArtPalette.Goo : debuffColors[(i + round) % debuffColors.Length];
                var seg = new GameObject("Chamber" + i);
                seg.transform.SetParent(_wheel, false);
                seg.AddComponent<MeshFilter>().sharedMesh = SegmentMesh(i, chambers);
                var renderer = seg.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = goo ? _gooMaterial : VisualFactory.Lit(Color.Lerp(color, ArtPalette.Ivory, 0.12f), 0.5f);
                // As fatias giram a 1 cm do fundo da roda: a sombra delas no fundo mudava a cada frame e cintilava
                // (sombra de geometria quase coplanar). Não projectam sombra — a roda continua a recebê-la.
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _segmentRenderers.Add(renderer);
                _segmentObjects.Add(seg);
                float mid = (i + 0.5f) * Mathf.PI * 2f / chambers;
                var label = VisualFactory.Text(seg.transform, goo ? "MORTE!" : "?",
                    new Vector3(Mathf.Cos(mid) * WheelRadius * 0.6f, Mathf.Sin(mid) * WheelRadius * 0.6f, -0.012f), goo ? 0.085f : 0.16f, goo ? ArtPalette.NavyDark : ArtPalette.NavyDark);
                label.transform.localRotation = Quaternion.Euler(0f, 0f, mid * Mathf.Rad2Deg - 90f);
                _segmentLabels.Add(label);
                // Separador entre fatias: régua plana e baça. As caixas arredondadas finas (1,8 cm) com verniz metálico
                // cintilavam com a roda sempre a girar (brilhos e contornos a saltar ao longo da aresta curva).
                var divider = VisualFactory.Primitive(PrimitiveType.Cube, "Divider", seg.transform,
                    new Vector3(Mathf.Cos(i * Mathf.PI * 2f / chambers) * WheelRadius * 0.5f, Mathf.Sin(i * Mathf.PI * 2f / chambers) * WheelRadius * 0.5f, -0.0025f),
                    new Vector3(0.026f, WheelRadius, 0.003f), _dividerMaterial);
                divider.transform.localRotation = Quaternion.Euler(0f, 0f, i * 360f / chambers - 90f);
                divider.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static Mesh SegmentMesh(int index, int count)
        {
            var mesh = new Mesh { name = "WheelSegment" };
            const int steps = 14;
            float a0 = index * Mathf.PI * 2f / count, a1 = (index + 1) * Mathf.PI * 2f / count;
            var verts = new List<Vector3> { Vector3.zero };
            for (int s = 0; s <= steps; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / (float)steps);
                verts.Add(new Vector3(Mathf.Cos(a) * WheelRadius, Mathf.Sin(a) * WheelRadius, 0f));
            }
            var tris = new List<int>();
            for (int s = 0; s < steps; s++)
            {
                // Frente virada para −Z (sentido horário visto de −Z).
                tris.Add(0);
                tris.Add(s + 2);
                tris.Add(s + 1);
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Bind(NetGame net)
        {
            net.RiskResolved += (player, outcome, def, eliminated) =>
            {
                _leverPull = 1f;
                // Tambor sorteado: um com bala (gosma) ou um de debuff; o de debuff passa a mostrar o efeito real.
                bool bang = outcome == RiskOutcome.Discharge;
                var options = new List<int>();
                for (int i = 0; i < _segmentLoaded.Count; i++)
                    if (_segmentLoaded[i] == bang) options.Add(i);
                if (options.Count == 0) return;
                int segment = options[Random.Range(0, options.Count)];
                if (!bang && def >= 0 && def < _catalog.Count)
                {
                    _segmentLabels[segment].text = _catalog[def].ShortLabel;
                    VisualFactory.SetColor(_segmentRenderers[segment].sharedMaterial, Color.Lerp(_catalog[def].Tint, ArtPalette.Ivory, 0.08f));
                }
                SpinTo(segment);
            };
        }

        void SpinTo(int segment)
        {
            float mid = (segment + 0.5f) * 360f / _segments;
            float target = 90f - mid;
            float current = _wheelAngle;
            float delta = Mathf.Repeat(target - current, 360f);
            _wheelFrom = current;
            _wheelTo = current + 360f * 3f + delta;
            _wheelTime = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            var net = NetGame.Instance;
            var view = net != null && net.HasSnapshot ? net.View : null;
            bool drawing = view != null && view.InMatch && view.Phase == MatchPhase.PunishmentDraw;
            bool tense = view != null && view.InMatch && (view.Phase == MatchPhase.AwaitTrigger || view.Phase == MatchPhase.RiskReveal);

            if (view != null && view.InMatch && _wheelTime < 0f && !tense) BuildChambers(view.Chambers, view.LoadedChambers, view.RoundIndex);

            // Roda: gira devagar em repouso; no resultado, desacelera até ao segmento certo.
            if (_wheelTime >= 0f)
            {
                _wheelTime += dt;
                float k = Mathf.Clamp01(_wheelTime / 2.6f);
                float ease = 1f - Mathf.Pow(1f - k, 4f);
                _wheelAngle = Mathf.Lerp(_wheelFrom, _wheelTo, ease);
                if (k >= 1f && _wheelTime > 5f) _wheelTime = -1f;
            }
            else if (!tense)
            {
                _wheelAngle += dt * (drawing ? 240f : 12f);
            }
            _wheel.localRotation = Quaternion.Euler(0f, 0f, _wheelAngle);

            _leverPull = Mathf.MoveTowards(_leverPull, 0f, dt * 1.2f);
            float leverAngle = _leverPull > 0f ? Mathf.Sin(Mathf.Clamp01(_leverPull) * Mathf.PI) * 70f : 0f;
            if (tense && view.Phase == MatchPhase.AwaitTrigger && _game.Hud != null) leverAngle = Mathf.Max(leverAngle, 25f * Mathf.Max(_game.Hud.TriggerHold, Mathf.PingPong(Time.time * 0.5f, 0.4f)));
            _lever.localRotation = Quaternion.Euler(leverAngle, 0f, 0f);

            // Lâmpadas em perseguição; mais rápidas no sorteio.
            float speed = drawing ? 18f : tense ? 9f : 3f;
            for (int i = 0; i < _bulbs.Count; i++)
            {
                bool lit = Mathf.Repeat(t * speed - i, 4f) < (drawing ? 2f : 2.8f);
                _bulbs[i].sharedMaterial = lit ? _bulbOn : _bulbOff;
            }

            UpdateReel(view, drawing, tense, dt);
        }

        void UpdateReel(MatchView view, bool drawing, bool tense, float dt)
        {
            var roster = new List<PlayerView>();
            int center = -1;
            if (view != null && view.InMatch)
            {
                if ((drawing || tense) && view.Candidates.Count > 0)
                {
                    foreach (int id in view.Candidates)
                    {
                        var p = view.GetPlayer(id);
                        if (p != null) roster.Add(p);
                    }
                    int highlight = drawing ? PunishmentPresentation.HighlightedCandidate(view) : view.Selected;
                    for (int i = 0; i < roster.Count; i++)
                        if (roster[i].Id == highlight) center = i;
                }
                else
                {
                    foreach (var p in view.Players)
                        if (p.IsActive) roster.Add(p);
                    if (roster.Count > 0) center = Mathf.FloorToInt(Time.time / 1.6f) % roster.Count;
                }
            }

            if (center != _lastCenter)
            {
                _lastCenter = center;
                _bounce = 1f;
            }
            _bounce = Mathf.MoveTowards(_bounce, 0f, dt * (drawing ? 9f : 4f));
            _centerFrameOn.SetColor("_EmissionColor", ArtPalette.Mustard * (tense ? 3f + Mathf.Sin(Time.time * 12f) * 2f : 3f));

            for (int slot = 0; slot < 3; slot++)
            {
                var panel = _panels[slot];
                if (roster.Count == 0 || center < 0)
                {
                    panel.Content.gameObject.SetActive(false);
                    continue;
                }
                panel.Content.gameObject.SetActive(true);
                int index = ((center + slot - 1) % roster.Count + roster.Count) % roster.Count;
                var p = roster[index];
                for (int s = 0; s < 3; s++) panel.Shapes[s].gameObject.SetActive(s == (int)p.Profile.Archetype);
                bool dim = slot != 1 && tense;
                VisualFactory.SetColor(panel.ShapeMaterial, dim ? Color.Lerp(p.Profile.Color, Color.gray, 0.6f) : p.Profile.Color);
                panel.Name.text = p.Name.ToUpperInvariant();
                var basePos = panel.Content.localPosition;
                panel.Content.localPosition = new Vector3(basePos.x, 2.8f + _bounce * 0.12f, basePos.z);
            }
        }
    }
}
