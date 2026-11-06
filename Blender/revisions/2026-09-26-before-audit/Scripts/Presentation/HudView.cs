using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// HUD em IMGUI no estilo dos concepts: placas rebitadas azul-petróleo/mostarda, fontes Anton/Oswald, ícones
    /// geométricos das cabeças. Lê só o MatchView replicado e envia intenções pelo NetGame.
    /// Este ficheiro tem o núcleo (estado, eventos, input, kit de desenho); os ecrãs estão nos parciais
    /// HudMenus (menu principal/multiplayer/personalizar), HudLobby (sala de espera), HudMatch (partida)
    /// e HudSettings (pausa/definições).
    /// </summary>
    public sealed partial class HudView : MonoBehaviour
    {
        const float VirtualHeight = 1080f;
        const float TriggerHoldSeconds = 0.75f;
        static readonly string[] EmoteTexts = { "HAHAHA!", "GG", "NÃÃÃO!", "Vingança…" };

        // Paleta de UI (concept 20/21).
        static readonly Color Navy = ArtPalette.Navy;
        static readonly Color NavyDark = ArtPalette.NavyDark;
        static readonly Color NavyLight = ArtPalette.NavyLight;
        static readonly Color Ivory = ArtPalette.Ivory;
        static readonly Color Mustard = ArtPalette.Mustard;
        static readonly Color Coral = ArtPalette.Coral;
        static readonly Color Teal = ArtPalette.Teal;
        static readonly Color Violet = ArtPalette.Violet;
        static readonly Color GoodGreen = ArtPalette.Green;
        static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f);
        static readonly Color Shade = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color SabotageColor = new Color(0.78f, 0.45f, 1f);

        GameBootstrap _game;
        NetGame _bound;
        Texture2D _triUp;
        Texture2D _triRight;
        float _vw;
        float _scale = 1f;
        readonly Dictionary<int, GUIStyle> _styles = new Dictionary<int, GUIStyle>();
        GUIStyle _field;

        readonly List<FeedLine> _feed = new List<FeedLine>();
        readonly List<Floater> _floaters = new List<Floater>();
        readonly Dictionary<int, Rect> _rowRects = new Dictionary<int, Rect>();
        readonly Dictionary<ulong, EmoteBubble> _emotes = new Dictionary<ulong, EmoteBubble>();
        string _banner;
        Color _bannerColor;
        float _bannerUntil;
        string _toast;
        float _toastUntil;
        int _lastHighlight = -1;
        bool _chatOpen;
        bool _focusChat;
        string _chatText = "";
        bool _triggerSent;
        string _triggerEvent = "";
        bool _eliminatedOverlay;
        float _celebrateUntil;

        struct FeedLine
        {
            public string Text;
            public Color Color;
            public float Time;
        }

        struct Floater
        {
            public int PlayerId;
            public string Text;
            public Color Color;
            public float Time;
        }

        struct EmoteBubble
        {
            public string Text;
            public float Time;
        }

        public bool ChatOpen => _chatOpen;

        /// <summary>Jogador destacado no sorteio/cadeira de risco (usado pelos avatares).</summary>
        public int HighlightedPlayer { get; private set; } = -1;

        /// <summary>Progresso 0..1 de "segurar para disparar" do jogador local (anima o cão do revólver e a alavanca).</summary>
        public float TriggerHold { get; private set; }

        /// <summary>Rato livre (Tab) para usar os painéis sem sair da sessão.</summary>
        public bool PanelCursor { get; set; }
        public bool EliminatedOverlayOpen => _eliminatedOverlay;

        /// <summary>A personagem de montra do menu festeja depois de uma alteração no visual.</summary>
        public bool ShowcaseCelebrate => Time.unscaledTime < _celebrateUntil;

        public bool WantsCursor
        {
            get
            {
                if (PanelCursor || _chatOpen || _settingsOpen || _roomSettingsOpen || _customizeOverlay || _eliminatedOverlay) return true;
                var net = NetGame.Instance;
                if (net == null || !net.HasSnapshot || !net.View.InMatch) return false;
                return net.View.Phase == MatchPhase.MatchOver;
            }
        }

        bool _wasInMatch;
        bool _modeInitialised;

        /// <summary>Clique na zona livre do ecrã da sala (entre a placa do código/jogadores e a barra de baixo).</summary>
        bool IsWorldClick(Vector2 screenPosition)
        {
            float y = (UnityEngine.Screen.height - screenPosition.y) / _scale;
            return y > 310f && y < _vh - 200f;
        }

        public void CloseChat()
        {
            _chatOpen = false;
            _chatText = "";
        }

        /// <summary>Esc: fecha a camada de UI mais recente. Devolve false se não havia nada para fechar.</summary>
        public bool HandleBack()
        {
            if (_settingsOpen)
            {
                _settingsOpen = false;
                return true;
            }
            if (_roomSettingsOpen)
            {
                _roomSettingsOpen = false;
                return true;
            }
            if (_customizeOverlay)
            {
                _customizeOverlay = false;
                return true;
            }
            if (_eliminatedOverlay)
            {
                _eliminatedOverlay = false;
                return true;
            }
            if (_game.Screen == AppScreen.Menu && _page != MenuPage.Main)
            {
                _page = _page == MenuPage.CreateRoom ? MenuPage.Multiplayer : MenuPage.Main;
                return true;
            }
            if (PanelCursor)
            {
                PanelCursor = false;
                return true;
            }
            return false;
        }

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            _triUp = MakeTriangle(false);
            _triRight = MakeTriangle(true);
            _addressInput = game.Settings.LastAddress;
            _codeInput = game.Settings.LastCode;
        }

        public void Bind(NetGame net)
        {
            _bound = net;
            _feed.Clear();
            _floaters.Clear();
            _emotes.Clear();
            _banner = null;
            _customizeOverlay = false;
            _roomSettingsOpen = false;
            _eliminatedOverlay = false;
            _modeInitialised = false;
            net.Feed += OnFeed;
            net.Toast += text => ShowToast(text);
            net.ShotResolved += OnShotResolved;
            net.RoundWonEvent += id => ShowBanner(NameOf(id) + " VENCE A RONDA!", Mustard, 2.6f);
            net.RiskResolved += OnRiskResolved;
            net.SabotageApplied += (buyer, target, def) =>
            {
                AddFloater(target, "SABOTADO: " + DefName(def), SabotageColor);
                if (net.LocalPlayer != null && net.LocalPlayer.Id == target) ShowBanner(NameOf(buyer) + " SABOTOU-TE: " + DefName(def).ToUpperInvariant(), SabotageColor, 2.6f);
            };
            net.PlayerEliminated += id =>
            {
                ShowBanner(NameOf(id) + " FOI ELIMINADO", Coral, 3f);
                if (net.LocalPlayer != null && net.LocalPlayer.Id == id) _eliminatedOverlay = true;
            };
            net.Emote += (client, emote) =>
            {
                _emotes[client] = new EmoteBubble { Text = EmoteTexts[Mathf.Clamp(emote, 0, EmoteTexts.Length - 1)], Time = Time.unscaledTime };
                // O boneco faz o gesto (antes só aparecia o balão).
                var player = NetPlayer.ForClient(client);
                if (player != null && player.Visual != null) player.Visual.PlayEmote(emote);
            };
            net.CoinsChanged += (id, delta, reason) =>
            {
                if (delta == 0 && !reason.Contains("máximo")) return;
                AddFloater(id, (delta >= 0 ? "+" : "") + delta + " " + reason, delta >= 0 ? Mustard : Coral);
            };
            net.TurnStarted += id =>
            {
                if (net.LocalPlayer != null && net.LocalPlayer.Id == id) ShowBanner("É A TUA VEZ — [E] PEGA NO TACO", Mustard, 2.2f);
            };
            net.Chat += (sender, text) =>
            {
                var slot = net.View.SlotForClient(sender);
                string who = slot != null ? Colored(slot.Profile.Name, slot.Profile.Color) : "Espectador";
                PushFeed(who + ": " + text, Color.white);
            };
        }

        bool _careerCounted;

        /// <summary>No fim de cada partida, guarda localmente jogos e vitórias (o anfitrião soma a vitória ao perfil de todos).</summary>
        void RecordCareer(NetGame net)
        {
            var view = net.View;
            bool over = view.InMatch && view.Phase == MatchPhase.MatchOver && view.MatchWinner >= 0;
            if (!over)
            {
                _careerCounted = false;
                return;
            }
            if (_careerCounted) return;
            _careerCounted = true;
            var me = net.LocalPlayer;
            if (me == null) return;
            _game.Settings.CareerMatches++;
            if (view.MatchWinner == me.Id) _game.Settings.CareerWins++;
            _game.Settings.Save();
        }

        string NameOf(int playerId)
        {
            var p = _bound != null ? _bound.View.GetPlayer(playerId) : null;
            return p != null ? p.Name : "?";
        }

        static bool ValidDef(int def) => NetGame.SharedCatalog != null && def >= 0 && def < NetGame.SharedCatalog.Count;
        static string DefName(int def) => ValidDef(def) ? NetGame.SharedCatalog[def].DisplayName : "efeito";
        static string DefLabel(int def) => ValidDef(def) ? NetGame.SharedCatalog[def].ShortLabel : "?";
        static Color DefTint(int def) => ValidDef(def) ? NetGame.SharedCatalog[def].Tint : Color.white;

        void PushFeed(string text, Color color)
        {
            _feed.Add(new FeedLine { Text = text, Color = color, Time = Time.unscaledTime });
            if (_feed.Count > 7) _feed.RemoveAt(0);
        }

        void OnFeed(string text, FeedKind kind)
        {
            Color c = kind == FeedKind.Good ? GoodGreen : kind == FeedKind.Bad ? Coral : kind == FeedKind.Risk ? new Color(1f, 0.55f, 0.4f)
                : kind == FeedKind.Sabotage ? SabotageColor : kind == FeedKind.Economy ? Mustard : Color.white;
            PushFeed(text, c);
        }

        void OnShotResolved(int shooter, FoulType foul, bool keep, bool won)
        {
            if (foul != FoulType.None) ShowBanner(PartyNineRules.Describe(foul).ToUpperInvariant(), Coral, 1.5f);
            else if (keep) ShowBanner(NameOf(shooter) + " CONTINUA!", GoodGreen, 1f);
            else if (!won) ShowBanner("PASSA A VEZ", Muted, 0.9f);
        }

        void OnRiskResolved(int playerId, RiskOutcome outcome, int def, bool eliminated)
        {
            if (outcome == RiskOutcome.Discharge) AddFloater(playerId, eliminated ? "ELIMINADO" : "-1 VIDA", Coral);
            else if (def >= 0) AddFloater(playerId, "RISCO: " + DefName(def), new Color(1f, 0.55f, 0.4f));
        }

        void AddFloater(int playerId, string text, Color color) =>
            _floaters.Add(new Floater { PlayerId = playerId, Text = text, Color = color, Time = Time.unscaledTime });

        public void ShowBanner(string text, Color color, float seconds)
        {
            _banner = text;
            _bannerColor = color;
            _bannerUntil = Time.unscaledTime + seconds;
        }

        public void ShowToast(string text, float seconds = 4f)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + seconds;
        }

        // ================================================================== input

        void Update()
        {
            var net = NetGame.Instance;
            HighlightedPlayer = -1;
            float dt = Time.unscaledDeltaTime;
            if (net == null || !net.HasSnapshot)
            {
                TriggerHold = 0f;
                return;
            }
            var view = net.View;
            var me = net.LocalPlayer;
            if (view.InMatch) _roomSettingsOpen = false;

            // Sala de espera: rato livre por omissão (os botões respondem logo); ao começar a partida volta a
            // primeira pessoa. Na sala, clicar no cenário (fora dos painéis) prende o rato para andar; Tab solta.
            if (view.InMatch != _wasInMatch || !_modeInitialised)
            {
                _modeInitialised = true;
                _wasInMatch = view.InMatch;
                PanelCursor = !view.InMatch;
            }
            var clickMouse = Mouse.current;
            if (!view.InMatch && PanelCursor && !_game.Paused && !_chatOpen && !_roomSettingsOpen && !_customizeOverlay && !_settingsOpen &&
                clickMouse != null && clickMouse.leftButton.wasPressedThisFrame && IsWorldClick(clickMouse.position.ReadValue()))
                PanelCursor = false;

            RecordCareer(net);

            if (view.InMatch)
            {
                if (view.Phase == MatchPhase.PunishmentDraw) HighlightedPlayer = PunishmentPresentation.HighlightedCandidate(view);
                else if (view.Phase == MatchPhase.AwaitTrigger || view.Phase == MatchPhase.RiskReveal) HighlightedPlayer = view.Selected;
            }

            if (view.Phase == MatchPhase.PunishmentDraw)
            {
                if (HighlightedPlayer != _lastHighlight)
                {
                    _lastHighlight = HighlightedPlayer;
                    _game.Sfx.PlayTick();
                }
            }
            else
            {
                _lastHighlight = -1;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool locked = !_game.CursorFree && !_game.Paused;

            // Segurar para puxar o gatilho (concept 11): Espaço ou clique, ~0,75 s.
            bool myTrigger = view.InMatch && view.Phase == MatchPhase.AwaitTrigger && me != null && view.Selected == me.Id;
            if (view.RiskEventId != _triggerEvent)
            {
                _triggerEvent = view.RiskEventId;
                _triggerSent = false;
            }
            if (myTrigger && !_triggerSent)
            {
                bool holding = locked && ((keyboard != null && keyboard.spaceKey.isPressed) || (mouse != null && mouse.leftButton.isPressed));
                TriggerHold = holding ? Mathf.Min(1f, TriggerHold + dt / TriggerHoldSeconds) : Mathf.MoveTowards(TriggerHold, 0f, dt * 1.5f);
                if (TriggerHold >= 1f)
                {
                    _triggerSent = true;
                    net.RequestTrigger();
                }
            }
            else if (!myTrigger)
            {
                TriggerHold = Mathf.MoveTowards(TriggerHold, 0f, dt * 3f);
            }

            if (keyboard != null && locked)
            {
                if (keyboard.tKey.wasPressedThisFrame)
                {
                    _chatOpen = true;
                    _focusChat = true;
                    _chatText = "";
                }
                if (keyboard.digit1Key.wasPressedThisFrame) net.SendEmote(0);
                if (keyboard.digit2Key.wasPressedThisFrame) net.SendEmote(1);
                if (keyboard.digit3Key.wasPressedThisFrame) net.SendEmote(2);
                if (keyboard.digit4Key.wasPressedThisFrame) net.SendEmote(3);
            }
            // "R — pronto" funciona com o rato livre ou preso (fora do chat e dos campos de texto).
            if (keyboard != null && !view.InMatch && !_chatOpen && !_game.Paused && GUIUtility.keyboardControl == 0 && keyboard.rKey.wasPressedThisFrame)
            {
                var slot = net.LocalSlot;
                if (slot != null && !net.IsLocalHost) net.SetReady(!slot.Ready);
            }
        }

        // ================================================================== OnGUI

        /// <summary>Testes automáticos (laboratório de animação): esconde toda a interface.</summary>
        public static bool DebugHideAll;

        void OnGUI()
        {
            if (_game == null || DebugHideAll) return;
            EnsureStyles();
            _scale = UnityEngine.Screen.height / VirtualHeight * Mathf.Clamp(_game.Settings.UiScale, 0.75f, 1.3f);
            _vw = UnityEngine.Screen.width / _scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(_scale, _scale, 1f));
            float vh = UnityEngine.Screen.height / _scale;
            _vh = vh;

            // Com um modal aberto, as camadas de baixo desenham-se mas não recebem cliques.
            _blockInput = _game.Paused || _settingsOpen;
            switch (_game.Screen)
            {
                case AppScreen.Menu:
                    DrawMenu();
                    break;
                case AppScreen.Connecting:
                    DrawConnecting();
                    break;
                case AppScreen.Session:
                    DrawSession();
                    break;
            }

            _blockInput = false;
            DrawToast();
            if (_game.Paused && !_settingsOpen) DrawPause();
            if (_settingsOpen) DrawSettings();
        }

        float _vh = VirtualHeight;
        bool _blockInput;

        bool Interactive => _game.CursorFree && !_blockInput;

        void EnsureStyles()
        {
            if (_field != null) return;
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 4, 4) };
            if (VisualFactory.BodyFont != null) _field.font = VisualFactory.BodyFont;
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Ivory;
            var bg = MakeTex(NavyDark);
            _field.normal.background = _field.focused.background = _field.hover.background = bg;
        }

        GUIStyle Style(int size, TextAnchor anchor, bool title, bool wrap = false)
        {
            int key = size * 1000 + (int)anchor * 10 + (title ? 1 : 0) + (wrap ? 5 : 0);
            if (_styles.TryGetValue(key, out var s)) return s;
            s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, richText = true, wordWrap = wrap, clipping = TextClipping.Overflow };
            s.padding = new RectOffset(0, 0, 0, 0);
            var font = title ? VisualFactory.TitleFont : VisualFactory.BodyFont;
            if (font != null) s.font = font;
            if (title && font == null) s.fontStyle = FontStyle.Bold;
            _styles[key] = s;
            return s;
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static Texture2D MakeTriangle(bool pointRight)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Triângulo equilátero arredondado por supersampling 4x4.
                float cover = 0f;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    float u = (x + (sx + 0.5f) / 4f) / n;
                    float v = (y + (sy + 0.5f) / 4f) / n;
                    if (pointRight)
                    {
                        // Vértice à direita: o eixo "ao longo" passa a ser u.
                        float tmp = u;
                        u = v;
                        v = tmp;
                    }
                    // Base em v=0.06, vértice em v=0.94 (textura GUI: y cresce para cima).
                    float halfWidth = Mathf.Lerp(0.47f, 0f, Mathf.InverseLerp(0.06f, 0.94f, v));
                    if (v >= 0.06f && v <= 0.94f && Mathf.Abs(u - 0.5f) <= halfWidth) cover += 1f / 16f;
                }
                px[y * n + x] = new Color(1f, 1f, 1f, cover);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // ================================================================== kit de desenho

        static void Round(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);

        static void Outline(Rect r, Color c, float width, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, width, radius);

        static void Fill(Rect r, Color c) => Round(r, c, 0f);

        static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - d * 2f, r.height - d * 2f);

        static Color Darker(Color c, float k = 0.55f) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        /// <summary>Placa rebitada dos concepts: sombra, aro escuro, face, brilho superior e rebites.</summary>
        void Plate(Rect r, Color face, bool rivets = true, float radius = 16f, bool glow = false)
        {
            Round(new Rect(r.x, r.y + 5f, r.width, r.height), new Color(0f, 0f, 0f, 0.4f), radius);
            if (glow) Outline(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(Mustard.r, Mustard.g, Mustard.b, 0.55f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f)), 4f, radius + 4f);
            Round(r, Darker(face, 0.5f), radius);
            var inner = Inset(r, 3f);
            Round(inner, face, radius - 3f);
            Round(new Rect(inner.x + 4f, inner.y + 3f, inner.width - 8f, Mathf.Min(inner.height * 0.42f, 40f)), new Color(1f, 1f, 1f, 0.09f), radius - 5f);
            if (!rivets || r.height < 30f) return;
            float s = 9f;
            float cy = r.height < 110f ? r.center.y - s * 0.5f : r.y + 10f;
            Rivet(new Rect(r.x + 10f, cy, s, s), face);
            Rivet(new Rect(r.xMax - 10f - s, cy, s, s), face);
            if (r.height >= 110f)
            {
                Rivet(new Rect(r.x + 10f, r.yMax - 10f - s, s, s), face);
                Rivet(new Rect(r.xMax - 10f - s, r.yMax - 10f - s, s, s), face);
            }
        }

        static void Rivet(Rect r, Color face)
        {
            Round(r, Darker(face, 0.35f), r.width * 0.5f);
            Round(new Rect(r.x + 2f, r.y + 1.5f, r.width - 4f, r.height - 4f), new Color(1f, 1f, 1f, 0.35f), r.width * 0.3f);
        }

        void T(Rect r, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool title = false, bool shadow = false, bool wrap = false)
        {
            var style = Style(size, anchor, title, wrap);
            if (shadow)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, 0.55f * color.a);
                GUI.Label(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, style);
            }
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        bool Hovered(Rect r) => Interactive && r.Contains(Event.current.mousePosition);

        /// <summary>Botão-placa. Cor da face define o tipo (mostarda = principal, azul = secundário, coral = perigo).</summary>
        bool Btn(Rect r, string text, Color face, Color textColor, bool enabled = true, int size = 30, IconKind icon = IconKind.None)
        {
            bool hover = enabled && Hovered(r);
            var f = enabled ? (hover ? Color.Lerp(face, Color.white, 0.14f) : face) : new Color(0.42f, 0.44f, 0.48f);
            var drawRect = hover && Event.current.type == EventType.Repaint && Mouse.current != null && Mouse.current.leftButton.isPressed ? new Rect(r.x, r.y + 2f, r.width, r.height) : r;
            Plate(drawRect, f, true, Mathf.Min(16f, r.height * 0.3f));
            float left = drawRect.x + 26f;
            if (icon != IconKind.None)
            {
                float s = Mathf.Min(34f, r.height * 0.52f);
                Icon(new Rect(drawRect.x + 30f, drawRect.center.y - s * 0.5f, s, s), icon, enabled ? textColor : Darker(textColor, 0.7f));
                left = drawRect.x + 30f + s + 18f;
                T(new Rect(left, drawRect.y, drawRect.xMax - left - 26f, drawRect.height), text, size, enabled ? textColor : new Color(0.25f, 0.26f, 0.3f), TextAnchor.MiddleLeft, true);
            }
            else
            {
                T(new Rect(drawRect.x + 20f, drawRect.y, drawRect.width - 40f, drawRect.height), text, size, enabled ? textColor : new Color(0.25f, 0.26f, 0.3f), TextAnchor.MiddleCenter, true);
            }

            bool old = GUI.enabled;
            GUI.enabled = enabled && Interactive;
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.enabled = old;
            if (clicked) _game.Sfx.PlayUi();
            return clicked;
        }

        bool Primary(Rect r, string text, bool enabled = true, int size = 30, IconKind icon = IconKind.None) => Btn(r, text, Mustard, NavyDark, enabled, size, icon);
        bool Secondary(Rect r, string text, bool enabled = true, int size = 30, IconKind icon = IconKind.None) => Btn(r, text, Navy, Ivory, enabled, size, icon);
        bool DangerBtn(Rect r, string text, bool enabled = true, int size = 30, IconKind icon = IconKind.None) => Btn(r, text, ArtPalette.Red, Color.white, enabled, size, icon);
        bool SuccessBtn(Rect r, string text, bool enabled = true, int size = 30, IconKind icon = IconKind.None) => Btn(r, text, GoodGreen, NavyDark, enabled, size, icon);

        /// <summary>Selector em comprimido (◂ valor ▸) — o padrão das definições do concept 17.</summary>
        int Cycler(Rect r, string value, int index, int count, bool highlight = false)
        {
            Round(r, NavyDark, r.height * 0.5f);
            var pill = new Rect(r.x + r.height, r.y + 4f, r.width - r.height * 2f, r.height - 8f);
            if (highlight) Round(pill, Mustard, pill.height * 0.5f);
            T(pill, value, 22, highlight ? NavyDark : Ivory, TextAnchor.MiddleCenter, true);
            var left = new Rect(r.x, r.y, r.height, r.height);
            var right = new Rect(r.xMax - r.height, r.y, r.height, r.height);
            float a = r.height * 0.36f;
            Arrow(new Rect(left.center.x - a * 0.5f, left.center.y - a * 0.5f, a, a), false, Hovered(left) ? Mustard : Ivory);
            Arrow(new Rect(right.center.x - a * 0.5f, right.center.y - a * 0.5f, a, a), true, Hovered(right) ? Mustard : Ivory);
            if (!Interactive) return index;
            if (GUI.Button(left, GUIContent.none, GUIStyle.none))
            {
                _game.Sfx.PlayUi();
                return (index + count - 1) % count;
            }
            if (GUI.Button(right, GUIContent.none, GUIStyle.none))
            {
                _game.Sfx.PlayUi();
                return (index + 1) % count;
            }
            return index;
        }

        bool OnOff(Rect r, bool value) => Cycler(r, value ? "Ligado" : "Desligado", value ? 1 : 0, 2, value) == 1;

        int Stepper(Rect r, int value, int min, int max, int step, string display = null)
        {
            int count = (max - min) / step + 1;
            int index = Mathf.Clamp((value - min) / step, 0, count - 1);
            int next = Cycler(r, display ?? value.ToString(), index, count);
            if (next == index) return value;
            // Sem dar a volta nos extremos: mais intuitivo para números.
            if (index == 0 && next == count - 1) return value;
            if (index == count - 1 && next == 0) return value;
            return min + next * step;
        }

        void Arrow(Rect r, bool right, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            if (!right) Rotate(180f, r.center);
            GUI.DrawTexture(r, _triRight);
            if (!right) Rotate(-180f, r.center);
            GUI.color = old;
        }

        /// <summary>RotateAroundPivot espera o pivô em píxeis de ecrã; a HUD desenha em coordenadas virtuais escaladas.</summary>
        void Rotate(float angle, Vector2 pivot) => GUIUtility.RotateAroundPivot(angle, pivot * _scale);

        float Slider(Rect r, float value, float min, float max)
        {
            float t = Mathf.InverseLerp(min, max, value);
            var track = new Rect(r.x, r.center.y - 7f, r.width, 14f);
            Round(track, NavyDark, 7f);
            Round(new Rect(track.x, track.y, Mathf.Max(14f, track.width * t), track.height), Mustard, 7f);
            var knob = new Rect(track.x + track.width * t - 13f, r.center.y - 13f, 26f, 26f);
            Round(knob, Ivory, 13f);
            Outline(knob, NavyDark, 3f, 13f);
            if (!Interactive) return value;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            if ((e.type == EventType.MouseDown && r.Contains(e.mousePosition)) || (e.type == EventType.MouseDrag && GUIUtility.hotControl == id))
            {
                GUIUtility.hotControl = id;
                value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - track.x) / track.width));
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
            return value;
        }

        // ------------------------------------------------------------------ ícones

        enum IconKind
        {
            None,
            Play,
            People,
            Head,
            Gear,
            Door,
            Plus,
            Key,
            Check,
            Globe,
            Lan,
            Copy,
            Music,
            Refresh
        }

        void Shape(Rect r, Archetype shape, Color c)
        {
            switch (shape)
            {
                case Archetype.Triangle:
                    var old = GUI.color;
                    GUI.color = c;
                    GUI.DrawTexture(r, _triUp);
                    GUI.color = old;
                    break;
                case Archetype.Circle:
                    Round(r, c, r.width * 0.5f);
                    break;
                default:
                    Round(Inset(r, r.width * 0.08f), c, r.width * 0.16f);
                    break;
            }
        }

        void Icon(Rect r, IconKind kind, Color c)
        {
            float w = r.width;
            switch (kind)
            {
                case IconKind.Play:
                    var old = GUI.color;
                    GUI.color = c;
                    GUI.DrawTexture(r, _triRight);
                    GUI.color = old;
                    break;
                case IconKind.People:
                    Round(new Rect(r.x + w * 0.1f, r.y + w * 0.05f, w * 0.36f, w * 0.36f), c, w * 0.18f);
                    Round(new Rect(r.x, r.y + w * 0.48f, w * 0.56f, w * 0.44f), c, w * 0.2f);
                    Round(new Rect(r.x + w * 0.56f, r.y + w * 0.1f, w * 0.32f, w * 0.32f), c, w * 0.16f);
                    Round(new Rect(r.x + w * 0.5f, r.y + w * 0.5f, w * 0.5f, w * 0.4f), c, w * 0.18f);
                    break;
                case IconKind.Head:
                    Round(new Rect(r.x + w * 0.15f, r.y, w * 0.7f, w * 0.7f), c, w * 0.12f);
                    Round(new Rect(r.x + w * 0.05f, r.y + w * 0.72f, w * 0.9f, w * 0.28f), c, w * 0.1f);
                    break;
                case IconKind.Gear:
                    for (int i = 0; i < 4; i++)
                    {
                        Rotate(i * 45f, r.center);
                        Fill(new Rect(r.center.x - w * 0.1f, r.y, w * 0.2f, w), c);
                        Rotate(-i * 45f, r.center);
                    }
                    Round(Inset(r, w * 0.14f), c, w * 0.36f);
                    Round(Inset(r, w * 0.34f), Navy, w * 0.16f);
                    break;
                case IconKind.Door:
                    Outline(new Rect(r.x + w * 0.15f, r.y, w * 0.6f, w), c, w * 0.12f, 2f);
                    Round(new Rect(r.x + w * 0.55f, r.y + w * 0.45f, w * 0.1f, w * 0.1f), c, w * 0.05f);
                    break;
                case IconKind.Plus:
                    Fill(new Rect(r.x + w * 0.4f, r.y, w * 0.2f, w), c);
                    Fill(new Rect(r.x, r.y + w * 0.4f, w, w * 0.2f), c);
                    break;
                case IconKind.Key:
                    Outline(new Rect(r.x, r.y + w * 0.25f, w * 0.5f, w * 0.5f), c, w * 0.12f, w * 0.25f);
                    Fill(new Rect(r.x + w * 0.45f, r.y + w * 0.45f, w * 0.55f, w * 0.12f), c);
                    Fill(new Rect(r.x + w * 0.8f, r.y + w * 0.45f, w * 0.1f, w * 0.3f), c);
                    break;
                case IconKind.Check:
                    Rotate(45f, r.center);
                    Fill(new Rect(r.center.x, r.y + w * 0.05f, w * 0.18f, w * 0.8f), c);
                    Fill(new Rect(r.center.x - w * 0.3f, r.y + w * 0.67f, w * 0.48f, w * 0.18f), c);
                    Rotate(-45f, r.center);
                    break;
                case IconKind.Globe:
                    Outline(r, c, w * 0.1f, w * 0.5f);
                    Outline(new Rect(r.x + w * 0.3f, r.y, w * 0.4f, w), c, w * 0.08f, w * 0.2f);
                    Fill(new Rect(r.x, r.center.y - w * 0.05f, w, w * 0.1f), c);
                    break;
                case IconKind.Lan:
                    Round(new Rect(r.x + w * 0.3f, r.y, w * 0.4f, w * 0.3f), c, 3f);
                    Fill(new Rect(r.center.x - w * 0.05f, r.y + w * 0.3f, w * 0.1f, w * 0.25f), c);
                    Fill(new Rect(r.x + w * 0.12f, r.y + w * 0.52f, w * 0.76f, w * 0.08f), c);
                    Round(new Rect(r.x, r.y + w * 0.7f, w * 0.3f, w * 0.3f), c, 3f);
                    Round(new Rect(r.x + w * 0.7f, r.y + w * 0.7f, w * 0.3f, w * 0.3f), c, 3f);
                    break;
                case IconKind.Copy:
                    Outline(new Rect(r.x + w * 0.3f, r.y, w * 0.7f, w * 0.7f), c, w * 0.1f, 4f);
                    Outline(new Rect(r.x, r.y + w * 0.3f, w * 0.7f, w * 0.7f), c, w * 0.1f, 4f);
                    break;
                case IconKind.Music:
                    Round(new Rect(r.x, r.y + w * 0.62f, w * 0.38f, w * 0.34f), c, w * 0.17f);
                    Fill(new Rect(r.x + w * 0.28f, r.y + w * 0.05f, w * 0.1f, w * 0.75f), c);
                    Fill(new Rect(r.x + w * 0.28f, r.y + w * 0.05f, w * 0.6f, w * 0.14f), c);
                    Fill(new Rect(r.x + w * 0.78f, r.y + w * 0.05f, w * 0.1f, w * 0.6f), c);
                    Round(new Rect(r.x + w * 0.5f, r.y + w * 0.52f, w * 0.38f, w * 0.34f), c, w * 0.17f);
                    break;
                case IconKind.Refresh:
                    Outline(r, c, w * 0.14f, w * 0.5f);
                    Fill(new Rect(r.x + w * 0.55f, r.y - w * 0.05f, w * 0.3f, w * 0.3f), Navy);
                    break;
            }
        }

        /// <summary>Retrato da personagem: disco azul com a forma da cabeça na cor do jogador e dois olhos.</summary>
        void HeadIcon(Rect r, PlayerProfile profile, bool dim = false, bool ring = true)
        {
            Round(r, NavyDark, r.width * 0.5f);
            if (ring) Outline(r, dim ? Muted : Ivory, Mathf.Max(2f, r.width * 0.05f), r.width * 0.5f);
            var inner = Inset(r, r.width * 0.2f);
            var color = dim ? Color.Lerp(profile.Color, Color.gray, 0.65f) : profile.Color;
            if (profile.Archetype == Archetype.Triangle) inner = new Rect(inner.x - r.width * 0.04f, inner.y - r.width * 0.06f, inner.width + r.width * 0.08f, inner.height + r.width * 0.08f);
            Shape(inner, profile.Archetype, color);
            float ew = r.width * 0.07f, eh = r.width * 0.15f;
            float ey = inner.y + inner.height * (profile.Archetype == Archetype.Triangle ? 0.55f : 0.36f);
            Round(new Rect(inner.center.x - ew * 1.9f, ey, ew, eh), NavyDark, ew * 0.5f);
            Round(new Rect(inner.center.x + ew * 0.9f, ey, ew, eh), NavyDark, ew * 0.5f);
        }

        void Coin(Rect r)
        {
            Round(r, Darker(Mustard, 0.6f), r.width * 0.5f);
            Round(Inset(r, r.width * 0.1f), Mustard, r.width * 0.4f);
            T(r, "$", Mathf.RoundToInt(r.height * 0.62f), Darker(Mustard, 0.45f), TextAnchor.MiddleCenter, true);
        }

        void EightBall(Rect r)
        {
            Round(r, new Color(0.06f, 0.06f, 0.08f), r.width * 0.5f);
            Round(new Rect(r.x + r.width * 0.12f, r.y + r.height * 0.08f, r.width * 0.36f, r.height * 0.22f), new Color(1f, 1f, 1f, 0.18f), r.width * 0.1f);
            var inner = new Rect(r.center.x - r.width * 0.26f, r.center.y - r.width * 0.26f, r.width * 0.52f, r.width * 0.52f);
            Round(inner, Ivory, inner.width * 0.5f);
            T(inner, "8", Mathf.RoundToInt(inner.height * 0.78f), NavyDark, TextAnchor.MiddleCenter, true);
        }

        /// <summary>Logótipo △○□ + bola 8 do concept.</summary>
        void Logo(Rect r)
        {
            Round(new Rect(r.x, r.y + 6f, r.width, r.height), new Color(0f, 0f, 0f, 0.4f), r.width * 0.5f);
            Round(r, NavyDark, r.width * 0.5f);
            Outline(r, Darker(Ivory, 0.8f), r.width * 0.03f, r.width * 0.5f);
            float s = r.width * 0.24f;
            float top = r.y + r.height * 0.14f;
            var old = GUI.color;
            GUI.color = Mustard;
            GUI.DrawTexture(new Rect(r.x + r.width * 0.12f, top + s * 0.1f, s, s), _triUp);
            GUI.color = old;
            Round(new Rect(r.center.x - s * 0.5f, top - s * 0.1f, s, s), Coral, s * 0.5f);
            Round(new Rect(r.xMax - r.width * 0.12f - s, top + s * 0.1f, s * 0.92f, s * 0.92f), Teal, s * 0.14f);
            float b = r.width * 0.4f;
            EightBall(new Rect(r.center.x - b * 0.5f, r.y + r.height * 0.46f, b, b));
        }

        void Pill(Rect r, string text, Color face, Color textColor, int size = 18)
        {
            Round(r, face, r.height * 0.5f);
            T(r, text, size, textColor, TextAnchor.MiddleCenter, true);
        }

        /// <summary>Taça pequena (histórico de vitórias).</summary>
        void Trophy(Rect r, Color c)
        {
            float w = r.width, h = r.height;
            Outline(new Rect(r.x, r.y + h * 0.08f, w * 0.36f, h * 0.36f), c, Mathf.Max(1.5f, w * 0.08f), w * 0.18f);
            Outline(new Rect(r.xMax - w * 0.36f, r.y + h * 0.08f, w * 0.36f, h * 0.36f), c, Mathf.Max(1.5f, w * 0.08f), w * 0.18f);
            Round(new Rect(r.x + w * 0.18f, r.y, w * 0.64f, h * 0.56f), c, w * 0.26f);
            Fill(new Rect(r.x + w * 0.18f, r.y, w * 0.64f, h * 0.2f), c);
            Fill(new Rect(r.center.x - w * 0.07f, r.y + h * 0.5f, w * 0.14f, h * 0.26f), c);
            Round(new Rect(r.x + w * 0.24f, r.y + h * 0.76f, w * 0.52f, h * 0.24f), c, h * 0.08f);
        }

        /// <summary>Taça + número de vitórias no histórico. Devolve o x onde o texto acaba.</summary>
        float CareerWins(Rect r, int wins, int size, Color c)
        {
            float icon = r.height * 0.9f;
            Trophy(new Rect(r.x, r.y + (r.height - icon) * 0.5f, icon, icon), c);
            string text = wins.ToString();
            var style = Style(size, TextAnchor.MiddleLeft, true);
            float tw = style.CalcSize(new GUIContent(text)).x;
            T(new Rect(r.x + icon + 6f, r.y, tw + 4f, r.height), text, size, c, TextAnchor.MiddleLeft, true);
            float end = r.x + icon + 6f + tw;
            if (r.width > 140f)
            {
                T(new Rect(end + 8f, r.y, r.width - (end - r.x) - 8f, r.height), wins == 1 ? "VITÓRIA" : "VITÓRIAS", Mathf.RoundToInt(size * 0.7f), Muted, TextAnchor.MiddleLeft, true);
            }
            return end;
        }

        void Pips(float x, float y, int filled, int total, float size, Color on)
        {
            for (int i = 0; i < total; i++)
            {
                var r = new Rect(x + i * (size + 6f), y, size, size);
                if (i < filled) Round(r, on, size * 0.5f);
                else Outline(r, Muted, 2.5f, size * 0.5f);
            }
        }

        static string Colored(string text, Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + text + "</color>";
        static string PlayerName(PlayerView p) => Colored(p.Name, p.Profile.Color);

        void TextField(Rect r, ref string value, int max, string controlName = null)
        {
            Round(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Darker(Navy, 0.6f), 10f);
            if (controlName != null) GUI.SetNextControlName(controlName);
            bool old = GUI.enabled;
            GUI.enabled = Interactive;
            _field.fontSize = Mathf.RoundToInt(r.height * 0.5f);
            value = GUI.TextField(r, value ?? "", max, _field);
            GUI.enabled = old;
        }
    }
}
