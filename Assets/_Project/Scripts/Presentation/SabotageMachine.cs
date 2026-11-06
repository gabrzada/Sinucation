using System.Collections.Generic;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Máquina física de sabotagem (concept 27). Construída com a frente em −Z local. Durante a janela de
    /// sabotagem, [E] faz zoom ao ecrã e a UI da loja é desenhada por cima do ecrã (HudView). Sempre que
    /// alguém sabota, sai uma carta de debuff pela ranhura.
    /// </summary>
    public sealed class SabotageMachine : Interactable
    {
        Transform _screen;
        TextMesh _screenTitle;
        TextMesh _screenBody;
        Transform _card;
        Material _cardMaterial;
        Material _screenMaterial;
        readonly List<Renderer> _bulbs = new List<Renderer>();
        readonly List<Material> _bulbOn = new List<Material>();
        Material _bulbOff;
        readonly List<Transform> _selectorButtons = new List<Transform>();
        readonly List<Material> _selectorMaterials = new List<Material>();
        Transform _bigButton;
        float _cardAnim = -1f;
        float _buttonPress;

        public override Vector3 FocusPoint => _screen.position;
        public override string Prompt => "<b>[E]</b> Usar a máquina de sabotagem";
        public override float Range => 2.6f;

        public override Pose ZoomPose
        {
            get
            {
                var normal = -_screen.forward;
                // A 0,9 m e FOV 50° o ecrã (0,7 m de altura) ocupa ~85% da vista: a UI da loja cabe lá dentro.
                return new Pose(_screen.position + normal * 0.9f, Quaternion.LookRotation(-normal, _screen.up));
            }
        }

        public override Vector3[] ScreenCorners
        {
            get
            {
                var s = _screen;
                return new[]
                {
                    s.TransformPoint(new Vector3(-0.5f, -0.5f, 0f)), s.TransformPoint(new Vector3(-0.5f, 0.5f, 0f)),
                    s.TransformPoint(new Vector3(0.5f, 0.5f, 0f)), s.TransformPoint(new Vector3(0.5f, -0.5f, 0f))
                };
            }
        }

        public override bool CanUse(NetGame net)
        {
            if (net == null || !net.HasSnapshot || !net.View.InMatch || net.View.Phase == MatchPhase.MatchOver) return false;
            var me = net.LocalPlayer;
            if (me == null || !me.IsActive) return false;
            // Sempre aberta: qualquer jogador vivo pode ir lá (os botões mostram se tem saldo/recarga).
            if (net.View.SabotageAnytime) return net.View.ShopOpen;
            return net.View.Phase == MatchPhase.Shop && !me.ShopPassed && me.PurchasesThisWindow < net.View.MaxPurchases;
        }

        public void Build()
        {
            var navy = VisualFactory.Lit(ArtPalette.Hex(0x2A4566), 0.4f, 0.2f);
            var navyDark = VisualFactory.Lit(ArtPalette.NavyDark, 0.4f);
            var mustard = VisualFactory.Lit(ArtPalette.Mustard, 0.5f, 0.35f);
            var ivory = VisualFactory.Lit(ArtPalette.Ivory, 0.4f);
            var steel = VisualFactory.Lit(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.9f);
            var box = LowPoly.BevelBox(0.08f);
            var root = transform;

            // Corpo de arcade modelado no Blender (Blender/sabotage.py); sem ele, volta às caixas.
            var bodyModel = FsModels.Get("sabotage", "SabotageBody");
            if (bodyModel != null)
            {
                var worn = VisualFactory.Textured(TextureFactory.WornMustard(), new Vector2(3f, 3f), 0.55f, 0.35f, 1f);
                var panelMat = VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(1f, 1f), 0.4f, 0.3f, 0.7f, new Color(0.8f, 0.85f, 0.95f));
                var dark = VisualFactory.Lit(new Color(0.02f, 0.025f, 0.035f), 0.3f);
                var rubber = VisualFactory.Lit(new Color(0.04f, 0.04f, 0.05f), 0.2f);
                var cache = new Dictionary<string, Material>
                {
                    { "navy", panelMat }, { "railMustard", worn }, { "mustard", mustard }, { "steel", steel }, { "ivory", ivory },
                    { "dark", dark }, { "rubber", rubber }, { "suitDark", navyDark }, { "coral", VisualFactory.Lit(ArtPalette.Coral, 0.5f) },
                    { "teal", VisualFactory.Lit(ArtPalette.Teal, 0.5f) }
                };
                FsModels.Spawn(bodyModel, root, Vector3.zero, Quaternion.identity, Vector3.one, n => cache.TryGetValue(n, out var m) ? m : navy);
            }
            else
            {
                LowPoly.Part(box, "Base", root, new Vector3(0f, 0.45f, 0f), new Vector3(1.3f, 0.9f, 0.9f), navy);
                LowPoly.Part(box, "Upper", root, new Vector3(0f, 1.45f, 0.12f), new Vector3(1.3f, 1.1f, 0.66f), navy);
                LowPoly.Part(box, "Marquee", root, new Vector3(0f, 2.12f, 0.02f), new Vector3(1.36f, 0.34f, 0.8f), mustard);
                foreach (float x in new[] { -0.66f, 0.66f })
                    LowPoly.Part(box, "Trim", root, new Vector3(x, 1.05f, 0f), new Vector3(0.06f, 2.1f, 0.94f), mustard);
                LowPoly.Part(box, "Plinth", root, new Vector3(0f, 0.04f, 0f), new Vector3(1.4f, 0.08f, 0.98f), mustard);
            }
            LowPoly.Part(box, "Sign", root, new Vector3(0f, 2.12f, -0.4f), new Vector3(1.18f, 0.26f, 0.03f), ivory);
            VisualFactory.Text(root, "SABOTAGEM", new Vector3(0f, 2.12f, -0.42f), 0.19f, ArtPalette.NavyDark);

            // Lâmpadas no topo: bola 8 ao centro e cúpulas de cor.
            _bulbOff = VisualFactory.Lit(new Color(0.25f, 0.25f, 0.28f), 0.8f);
            Color[] domeColors = { ArtPalette.Teal, ArtPalette.Coral, ArtPalette.Coral, ArtPalette.Teal };
            float[] domeX = { -0.5f, -0.25f, 0.25f, 0.5f };
            for (int i = 0; i < 4; i++)
            {
                var on = VisualFactory.Emissive(domeColors[i], 3f);
                _bulbOn.Add(on);
                var dome = LowPoly.Part(LowPoly.Icosphere(1), "Dome", root, new Vector3(domeX[i], 2.34f, 0.02f), Vector3.one * 0.16f, on);
                _bulbs.Add(dome.GetComponent<Renderer>());
            }
            var eight = LowPoly.Part(ToyMeshes.Sphere, "EightBall", root, new Vector3(0f, 2.4f, 0.02f), Vector3.one * 0.24f, VisualFactory.Lit(Color.white, 0.9f));
            VisualFactory.SetTexture(eight.GetComponent<Renderer>().sharedMaterial, VisualFactory.BallTexture(8));

            // Ecrã inclinado.
            var screenPivot = LowPoly.Pivot("ScreenPivot", root, new Vector3(0f, 1.55f, -0.22f));
            screenPivot.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            LowPoly.Part(box, "Bezel", screenPivot, Vector3.zero, new Vector3(1.08f, 0.8f, 0.06f), navyDark);
            _screenMaterial = VisualFactory.Emissive(new Color(0.08f, 0.12f, 0.2f), 1f);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(screen.GetComponent<Collider>());
            screen.name = "Screen";
            screen.transform.SetParent(screenPivot, false);
            screen.transform.localPosition = new Vector3(0f, 0f, -0.032f);
            screen.transform.localScale = new Vector3(0.96f, 0.7f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = _screenMaterial;
            _screen = screen.transform;
            var textPivot = LowPoly.Pivot("ScreenText", screenPivot, new Vector3(0f, 0f, -0.036f));
            _screenTitle = VisualFactory.Text(textPivot, "LOJA FECHADA", new Vector3(0f, 0.12f, 0f), 0.11f, ArtPalette.Mustard);
            _screenBody = VisualFactory.Text(textPivot, "", new Vector3(0f, -0.1f, 0f), 0.055f, ArtPalette.Ivory, TextAnchor.MiddleCenter, false);

            // Painel de controlo: selectores por jogador, botão grande, ranhura de moedas.
            var panel = LowPoly.Part(box, "ControlPanel", root, new Vector3(0f, 0.98f, -0.38f), new Vector3(1.2f, 0.12f, 0.36f), navyDark);
            panel.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                var mat = VisualFactory.Lit(ArtPalette.Heads[i], 0.6f);
                _selectorMaterials.Add(mat);
                var b = LowPoly.Part(LowPoly.Limb(10, 0.045f, 0.05f, 0.04f), "Selector", panel.transform, new Vector3(-0.38f + i * 0.1f, 0.62f, -0.1f), new Vector3(1f / 1.2f, 1f / 0.12f, 1f / 0.36f), mat);
                b.transform.localPosition = new Vector3(-0.3f + i * 0.08f, 0.9f, -0.1f);
                _selectorButtons.Add(b.transform);
            }
            _bigButton = LowPoly.Part(LowPoly.Icosphere(1), "BigButton", root, new Vector3(0.36f, 1.07f, -0.44f), new Vector3(0.18f, 0.1f, 0.18f), VisualFactory.Lit(ArtPalette.Red, 0.85f)).transform;
            LowPoly.Part(LowPoly.Limb(12, 0.12f, 0.12f, 0.04f), "ButtonRing", root, new Vector3(0.36f, 1.06f, -0.44f), Vector3.one, mustard);
            LowPoly.Part(box, "CoinSlot", root, new Vector3(-0.42f, 0.7f, -0.46f), new Vector3(0.16f, 0.24f, 0.03f), steel);
            LowPoly.Part(box, "CoinSlotHole", root, new Vector3(-0.42f, 0.74f, -0.475f), new Vector3(0.02f, 0.1f, 0.01f), navyDark);
            VisualFactory.Text(root, "MOEDAS", new Vector3(-0.42f, 0.86f, -0.465f), 0.04f, ArtPalette.Mustard);

            // Saída de cartas de debuff.
            LowPoly.Part(box, "CardSlotFrame", root, new Vector3(0.1f, 0.5f, -0.46f), new Vector3(0.5f, 0.2f, 0.04f), mustard);
            LowPoly.Part(box, "CardSlot", root, new Vector3(0.1f, 0.5f, -0.48f), new Vector3(0.36f, 0.05f, 0.02f), VisualFactory.Emissive(new Color(1f, 0.2f, 0.5f), 2f));
            VisualFactory.Text(root, "SAÍDA DE CARTAS", new Vector3(0.1f, 0.64f, -0.485f), 0.045f, ArtPalette.NavyDark);
            _cardMaterial = VisualFactory.Emissive(ArtPalette.Violet, 1.2f);
            _card = LowPoly.Part(box, "Card", root, new Vector3(0.1f, 0.5f, -0.44f), new Vector3(0.28f, 0.01f, 0.2f), _cardMaterial).transform;
            _card.gameObject.SetActive(false);

            var col = gameObject.AddComponent<BoxCollider>();
            // Vai até à parede de trás (+1,3): não deixa um corredor atrás da máquina.
            col.center = new Vector3(0f, 1.2f, 0.4f);
            col.size = new Vector3(1.4f, 2.4f, 1.8f);
            InteractionSystem.All.Add(this);
        }

        public void Bind(NetGame net)
        {
            net.SabotageApplied += (buyer, target, def) =>
            {
                _cardAnim = 0f;
                _buttonPress = 1f;
                var catalog = NetGame.SharedCatalog;
                if (catalog != null && def >= 0 && def < catalog.Count) VisualFactory.SetEmission(_cardMaterial, catalog[def].Tint * 1.5f);
            };
        }

        void OnDestroy() => InteractionSystem.All.Remove(this);

        void Update()
        {
            var net = NetGame.Instance;
            var view = net != null && net.HasSnapshot ? net.View : null;
            bool open = view != null && view.InMatch && view.ShopOpen && view.Phase != MatchPhase.MatchOver;
            var me = open && NetGame.Instance != null ? NetGame.Instance.LocalPlayer : null;

            float t = Time.time;
            for (int i = 0; i < _bulbs.Count; i++)
            {
                bool lit = open ? Mathf.Repeat(t * 6f + i, 2f) < 1f : Mathf.Repeat(t * 0.8f + i * 0.25f, 1f) < 0.6f;
                _bulbs[i].sharedMaterial = lit ? _bulbOn[i] : _bulbOff;
            }

            if (open)
            {
                _screenTitle.text = "SABOTAGEM";
                _screenBody.text = view.SabotageAnytime
                    ? (me != null && me.SabotageCooldown > 0f ? "a recarregar " + Mathf.CeilToInt(me.SabotageCooldown) + " s" : "[E] escolher alvo")
                    : Mathf.CeilToInt(view.PhaseRemaining) + " s\n[E] escolher alvo";
                VisualFactory.SetEmission(_screenMaterial, Color.Lerp(new Color(0.1f, 0.14f, 0.3f), new Color(0.3f, 0.1f, 0.35f), Mathf.PingPong(t, 1f)));
            }
            else
            {
                _screenTitle.text = view != null && view.InMatch ? "FECHADA" : "SABOTAGEM";
                _screenBody.text = view != null && view.InMatch ? "sabotagem desligada" : "abre quando a partida começar";
                VisualFactory.SetEmission(_screenMaterial, new Color(0.06f, 0.09f, 0.16f));
            }

            int players = view != null ? Mathf.Min(6, view.InMatch ? view.Players.Count : view.Slots.Count) : 0;
            for (int i = 0; i < _selectorButtons.Count; i++)
            {
                _selectorButtons[i].gameObject.SetActive(i < players);
                if (i >= players) continue;
                var profile = view.InMatch ? view.Players[i].Profile : view.Slots[i].Profile;
                VisualFactory.SetColor(_selectorMaterials[i], profile.Color);
            }

            _buttonPress = Mathf.MoveTowards(_buttonPress, 0f, Time.deltaTime * 4f);
            _bigButton.localScale = new Vector3(0.18f, 0.1f * (1f - _buttonPress * 0.5f), 0.18f);

            if (_cardAnim >= 0f)
            {
                _cardAnim += Time.deltaTime;
                _card.gameObject.SetActive(true);
                float slide = Mathf.Clamp01(_cardAnim / 0.5f);
                _card.localPosition = new Vector3(0.1f, 0.5f - slide * 0.05f, -0.44f - slide * 0.22f);
                _card.localRotation = Quaternion.Euler(slide * 35f, 0f, 0f);
                if (_cardAnim > 3f)
                {
                    _cardAnim = -1f;
                    _card.gameObject.SetActive(false);
                }
            }
        }
    }
}
