using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Menu principal, treino, multiplayer (navegador de salas), criar sala e personalizar (concepts 02–06, 33).</summary>
    public sealed partial class HudView
    {
        enum MenuPage
        {
            Main,
            Practice,
            Multiplayer,
            CreateRoom,
            Customize
        }

        MenuPage _page = MenuPage.Main;
        string _codeInput = "";
        string _addressInput = "127.0.0.1";
        string _portInput = "7777";
        int _practiceBots = 3;
        RoomRequest _create;
        float _nextRefresh;
        bool _customizeOverlay;
        readonly List<RoomListing> _roomScratch = new List<RoomListing>();

        /// <summary>Testes automáticos (AutoPilot -fsMenuShots): muda de página do menu.</summary>
        public void DebugShow(string page)
        {
            _settingsOpen = false;
            _hideMenu = false;
            switch (page)
            {
                case "multiplayer":
                    OpenMultiplayer();
                    break;
                case "create":
                    _create = new RoomRequest { Name = "Mesa do " + _game.Settings.PlayerName };
                    _page = MenuPage.CreateRoom;
                    break;
                case "customize":
                    _page = MenuPage.Customize;
                    break;
                case "settings":
                    _page = MenuPage.Main;
                    OpenSettings();
                    break;
                case "hidden":
                    _page = MenuPage.Main;
                    _hideMenu = true;
                    break;
                default:
                    _page = MenuPage.Main;
                    break;
            }
        }

        bool _hideMenu;

        void DrawMenu()
        {
            if (_hideMenu) return; // retratos automáticos: só a cena 3D
            // Escurece a esquerda para os botões lerem bem sobre a cena 3D.
            for (int i = 0; i < 12; i++)
                Fill(new Rect(i * 60f, 0f, 60f, _vh), new Color(0.02f, 0.03f, 0.06f, 0.55f * (1f - i / 12f)));

            if (_page != MenuPage.Customize) Logo(new Rect(70f, 44f, 210f, 210f));
            switch (_page)
            {
                case MenuPage.Main:
                    DrawMainMenu();
                    break;
                case MenuPage.Practice:
                    DrawPractice();
                    break;
                case MenuPage.Multiplayer:
                    DrawMultiplayer();
                    break;
                case MenuPage.CreateRoom:
                    DrawCreateRoom();
                    break;
                case MenuPage.Customize:
                    DrawCustomize(new Rect(60f, 60f, 640f, _vh - 120f), true);
                    break;
            }

            if (!string.IsNullOrEmpty(_game.StatusMessage) && _page != MenuPage.Customize)
            {
                var r = new Rect(60f, _vh - 110f, Mathf.Min(1100f, _vw - 120f), 64f);
                Plate(r, new Color(0.45f, 0.1f, 0.1f), true, 12f);
                T(Inset(r, 26f), _game.StatusMessage, 20, Ivory, TextAnchor.MiddleLeft, false, false, true);
            }
        }

        void DrawMainMenu()
        {
            T(new Rect(300f, 70f, 900f, 110f), "FRIEND SLOP", 96, Ivory, TextAnchor.MiddleLeft, true, true);
            T(new Rect(304f, 170f, 900f, 40f), "BILHAR  ·  ROLETA  ·  SABOTAGEM", 26, Mustard, TextAnchor.MiddleLeft, true, true);

            float x = 60f, y = 300f, w = 560f, h = 92f, gap = 18f;
            if (Primary(new Rect(x, y, w, h), "JOGAR", true, 40, IconKind.Play)) _page = MenuPage.Practice;
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "MULTIPLAYER", true, 40, IconKind.People)) OpenMultiplayer();
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "PERSONALIZAR", true, 40, IconKind.Head)) _page = MenuPage.Customize;
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "DEFINIÇÕES", true, 40, IconKind.Gear)) OpenSettings();
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "SAIR", true, 40, IconKind.Door)) _game.Quit();

            T(new Rect(_vw - 520f, _vh - 44f, 500f, 30f), "F1 ajuda  ·  sons: Kenney (CC0)  ·  música: OpenGameArt (CC0)", 16, Muted, TextAnchor.MiddleRight);
        }

        void DrawPractice()
        {
            var panel = new Rect(60f, 300f, 620f, 430f);
            Plate(panel, Navy);
            T(new Rect(panel.x + 36f, panel.y + 20f, 500f, 60f), "TREINO LOCAL", 44, Mustard, TextAnchor.MiddleLeft, true);
            T(new Rect(panel.x + 36f, panel.y + 80f, 550f, 70f), "Joga sozinho contra bots, com as mesmas regras, loja de sabotagem e roleta.", 21, Ivory, TextAnchor.UpperLeft, false, false, true);
            float y = panel.y + 170f;
            SettingRow(panel.x + 30f, y, panel.width - 60f, "BOTS");
            _practiceBots = Stepper(new Rect(panel.xMax - 300f, y + 8f, 250f, 44f), _practiceBots, 2, 5, 1);
            y += 90f;
            if (Primary(new Rect(panel.x + 30f, y, panel.width - 60f, 86f), "COMEÇAR", !_game.Session.Busy, 38, IconKind.Play)) _game.StartPractice(_practiceBots);
            if (Secondary(new Rect(60f, panel.yMax + 24f, 300f, 70f), "VOLTAR", true, 28)) _page = MenuPage.Main;
        }

        void SettingRow(float x, float y, float w, string label)
        {
            Round(new Rect(x, y, w, 60f), new Color(0.05f, 0.08f, 0.12f, 0.8f), 10f);
            T(new Rect(x + 22f, y, w * 0.5f, 60f), label, 25, Ivory, TextAnchor.MiddleLeft, true);
        }

        // ------------------------------------------------------------------ multiplayer

        void OpenMultiplayer()
        {
            _page = MenuPage.Multiplayer;
            _game.Session.Lan.StartListening();
            _nextRefresh = 0f;
        }

        void DrawMultiplayer()
        {
            var session = _game.Session;
            bool busy = session.Busy;
            if (Time.unscaledTime > _nextRefresh && !session.Querying)
            {
                _nextRefresh = Time.unscaledTime + 12f;
                _ = session.RefreshOnlineRooms();
            }

            float x = 60f, y = 290f, w = 560f, h = 88f, gap = 16f;
            if (Primary(new Rect(x, y, w, h), "PARTIDA RÁPIDA", !busy, 36, IconKind.Play)) _game.QuickMatch();
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "CRIAR SALA", !busy, 36, IconKind.People))
            {
                _create = new RoomRequest { Name = string.IsNullOrWhiteSpace(_game.Settings.LastRoomName) ? "Mesa de " + _game.Settings.PlayerName : _game.Settings.LastRoomName };
                _page = MenuPage.CreateRoom;
            }
            y += h + gap;
            if (Secondary(new Rect(x, y, w, h), "SALA PRIVADA (CÓDIGO)", !busy, 32, IconKind.Key)) GUI.FocusControl("code");
            y += h + gap;
            if (Secondary(new Rect(x, y, 270f, 70f), "VOLTAR", true, 26)) _page = MenuPage.Main;

            DrawRoomBrowser(new Rect(Mathf.Max(660f, _vw - 900f), 60f, Mathf.Min(840f, _vw - 700f), _vh - 250f));
            DrawJoinBar(new Rect(40f, _vh - 150f, _vw - 80f, 110f), busy);
        }

        void DrawRoomBrowser(Rect r)
        {
            var session = _game.Session;
            Plate(r, Navy);
            Icon(new Rect(r.x + 34f, r.y + 30f, 38f, 38f), IconKind.Globe, Ivory);
            T(new Rect(r.x + 90f, r.y + 18f, 400f, 60f), "SALAS ABERTAS", 40, Ivory, TextAnchor.MiddleLeft, true);
            if (Secondary(new Rect(r.xMax - 230f, r.y + 20f, 200f, 58f), session.Querying ? "A PROCURAR…" : "ACTUALIZAR", !session.Querying, 22))
            {
                _nextRefresh = Time.unscaledTime + 12f;
                _ = session.RefreshOnlineRooms();
            }

            _roomScratch.Clear();
            _roomScratch.AddRange(session.OnlineRooms);
            _roomScratch.AddRange(session.Lan.Rooms);

            float y = r.y + 100f;
            if (_roomScratch.Count == 0)
            {
                string text = session.Querying ? "A procurar salas…" :
                    "Não há salas públicas agora.\nCria uma com CRIAR SALA, ou entra numa sala privada com o código que o anfitrião te enviou.";
                T(new Rect(r.x + 40f, y + 20f, r.width - 80f, 140f), text, 22, Muted, TextAnchor.UpperLeft, false, false, true);
            }
            int shown = 0;
            foreach (var room in _roomScratch)
            {
                if (y + 92f > r.yMax - 70f) break;
                var row = new Rect(r.x + 24f, y, r.width - 48f, 84f);
                Round(row, NavyDark, 12f);
                Icon(new Rect(row.x + 22f, row.center.y - 17f, 34f, 34f), room.Online ? IconKind.Globe : IconKind.Lan, room.Online ? Teal : Mustard);
                T(new Rect(row.x + 76f, row.y + 8f, row.width - 380f, 40f), room.Name, 28, Ivory, TextAnchor.MiddleLeft, true);
                T(new Rect(row.x + 76f, row.y + 46f, row.width - 380f, 30f), (room.Online ? "Online" : "Rede local · " + room.Address) + "  ·  " + room.Players + "/" + room.MaxPlayers + " jogadores", 18, Muted);
                bool full = room.Players >= room.MaxPlayers;
                Pill(new Rect(row.xMax - 330f, row.center.y - 18f, 130f, 36f), room.InMatch ? "EM JOGO" : full ? "CHEIA" : "À ESPERA", room.InMatch || full ? NavyLight : GoodGreen, room.InMatch || full ? Ivory : NavyDark);
                if (Primary(new Rect(row.xMax - 184f, row.y + 12f, 168f, 60f), "ENTRAR", !room.InMatch && !full && !session.Busy, 24)) _game.JoinListing(room);
                y += 94f;
                shown++;
            }
            T(new Rect(r.x + 34f, r.yMax - 60f, r.width - 68f, 40f), "Salas privadas (fechadas) não aparecem aqui — entra com o código da sala.", 18, Muted);
        }

        void DrawJoinBar(Rect r, bool busy)
        {
            Plate(r, Navy);
            float x = r.x + 40f, cy = r.center.y;
            Icon(new Rect(x, cy - 18f, 36f, 36f), IconKind.Key, Mustard);
            T(new Rect(x + 50f, r.y, 200f, r.height), "CÓDIGO DA SALA", 24, Ivory, TextAnchor.MiddleLeft, true);
            x += 250f;
            string code = _codeInput;
            TextField(new Rect(x, cy - 30f, 250f, 60f), ref code, 12, "code");
            _codeInput = (code ?? "").ToUpperInvariant().Trim();
            x += 266f;
            bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "code";
            if (Primary(new Rect(x, cy - 34f, 190f, 68f), "ENTRAR", !busy && _codeInput.Length >= 4, 26) || (enter && !busy && _codeInput.Length >= 4)) _game.JoinOnline(_codeInput);
            x += 220f;

            if (r.xMax - x < 640f) return;
            Fill(new Rect(x, r.y + 18f, 3f, r.height - 36f), NavyLight);
            x += 30f;
            Icon(new Rect(x, cy - 18f, 36f, 36f), IconKind.Lan, Teal);
            T(new Rect(x + 50f, r.y, 90f, r.height), "IP", 24, Ivory, TextAnchor.MiddleLeft, true);
            x += 100f;
            TextField(new Rect(x, cy - 30f, 230f, 60f), ref _addressInput, 64);
            x += 240f;
            TextField(new Rect(x, cy - 30f, 110f, 60f), ref _portInput, 5);
            x += 124f;
            if (Secondary(new Rect(x, cy - 34f, 170f, 68f), "LIGAR", !busy, 26)) _game.JoinLan(_addressInput, ParsePort());
        }

        ushort ParsePort() => ushort.TryParse(_portInput, out ushort p) && p > 0 ? p : SessionService.DefaultPort;

        // ------------------------------------------------------------------ criar sala (concept 06)

        void DrawCreateRoom()
        {
            if (_create == null) _create = new RoomRequest();
            var c = _create;
            var head = new Rect(60f, 50f, 700f, 130f);
            Plate(head, Navy);
            Logo(new Rect(head.x + 22f, head.y + 12f, 106f, 106f));
            Fill(new Rect(head.x + 150f, head.y + 26f, 4f, head.height - 52f), Ivory);
            T(new Rect(head.x + 178f, head.y, 500f, head.height), "CRIAR SALA", 64, Ivory, TextAnchor.MiddleLeft, true, true);

            var body = new Rect(60f, head.yMax + 14f, 700f, c.Online ? 670f : 736f);
            Plate(body, NavyDark, true, 16f);
            float x = body.x + 22f, w = body.width - 44f, y = body.y + 22f, ctl = body.x + 290f, cw = body.xMax - 22f - ctl - 10f;

            SettingRow(x, y, w, "NOME DA SALA");
            TextField(new Rect(ctl, y + 8f, cw, 44f), ref c.Name, 24);
            y += 66f;

            SettingRow(x, y, w, "LIGAÇÃO");
            int net = Segments(new Rect(ctl, y + 8f, cw, 44f), new[] { "ONLINE", "REDE LOCAL" }, c.Online ? 0 : 1);
            c.Online = net == 0;
            y += 66f;

            SettingRow(x, y, w, "MÁX. JOGADORES");
            c.MaxPlayers = 3 + Segments(new Rect(ctl, y + 8f, cw, 44f), new[] { "3", "4", "5", "6" }, c.MaxPlayers - 3);
            y += 66f;

            SettingRow(x, y, w, "RONDAS");
            c.Rounds = Stepper(new Rect(ctl, y + 8f, cw, 44f), c.Rounds, 1, 12, 1);
            y += 66f;

            SettingRow(x, y, w, "BOLAS");
            c.BallCount = BallStepper(new Rect(ctl, y + 8f, cw, 44f), c.BallCount);
            y += 66f;

            SettingRow(x, y, w, "SALA PRIVADA");
            c.Private = Switch(new Rect(body.xMax - 22f - 110f, y + 10f, 96f, 40f), c.Private);
            y += 66f;

            SettingRow(x, y, w, "DEBUFFS");
            c.EnableDebuffs = Switch(new Rect(body.xMax - 22f - 110f, y + 10f, 96f, 40f), c.EnableDebuffs);
            y += 66f;

            SettingRow(x, y, w, "SABOTAGEM");
            c.EnableSabotage = Switch(new Rect(body.xMax - 22f - 110f, y + 10f, 96f, 40f), c.EnableSabotage);
            y += 66f;

            if (!c.Online)
            {
                SettingRow(x, y, w, "BOTS INICIAIS");
                c.Bots = Stepper(new Rect(ctl, y + 8f, cw, 44f), c.Bots, 0, 5, 1);
                y += 66f;
            }

            string hint = c.Online
                ? (c.Private ? "Privada: só entra quem tiver o código." : "Pública: aparece em SALAS ABERTAS e na partida rápida.") + " Relay da Unity — funciona pela internet."
                : (c.Private ? "Privada: não é anunciada na rede; entra-se pelo IP." : "Pública: aparece em SALAS ABERTAS para quem estiver na mesma rede.");
            T(new Rect(x + 6f, y + 4f, w - 12f, 60f), hint, 18, Muted, TextAnchor.UpperLeft, false, false, true);

            if (Primary(new Rect(60f, body.yMax + 18f, 700f, 96f), "CRIAR SALA", !_game.Session.Busy && !string.IsNullOrWhiteSpace(c.Name), 42, IconKind.Play))
            {
                c.Name = c.Name.Trim();
                _game.HostRoom(c);
            }
            if (Secondary(new Rect(780f, body.yMax + 30f, 220f, 72f), "VOLTAR", true, 26)) _page = MenuPage.Multiplayer;
        }

        int BallStepper(Rect r, int value)
        {
            // 0 = automático; depois 3..15.
            int index = value <= 0 ? 0 : value - 2;
            string label = value <= 0 ? "AUTO" : value.ToString();
            int next = Cycler(r, label, index, 14);
            return next == 0 ? 0 : next + 2;
        }

        int Segments(Rect r, string[] labels, int selected)
        {
            float gap = 8f, w = (r.width - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var b = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                bool on = i == selected;
                bool hover = Hovered(b);
                Round(b, Darker(on ? Mustard : Navy, 0.5f), 10f);
                Round(Inset(b, 2f), on ? Mustard : hover ? NavyLight : Navy, 8f);
                T(b, labels[i], 22, on ? NavyDark : Ivory, TextAnchor.MiddleCenter, true);
                if (Interactive && GUI.Button(b, GUIContent.none, GUIStyle.none))
                {
                    _game.Sfx.PlayUi();
                    selected = i;
                }
            }
            return selected;
        }

        bool Switch(Rect r, bool on)
        {
            Round(r, on ? Mustard : NavyLight, r.height * 0.5f);
            float k = r.height - 8f;
            Round(new Rect(on ? r.xMax - k - 4f : r.x + 4f, r.y + 4f, k, k), Ivory, k * 0.5f);
            if (Interactive && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                _game.Sfx.PlayUi();
                on = !on;
            }
            return on;
        }

        // ------------------------------------------------------------------ personalizar (concept 02)

        void DrawCustomize(Rect panel, bool fromMenu)
        {
            var s = _game.Settings;
            Plate(panel, Navy);
            T(new Rect(panel.x + 34f, panel.y + 18f, panel.width - 60f, 70f), "PERSONALIZAR", 52, Mustard, TextAnchor.MiddleLeft, true, true);
            float x = panel.x + 30f, w = panel.width - 60f, y = panel.y + 100f;

            T(new Rect(x, y, 200f, 40f), "NOME", 24, Ivory, TextAnchor.MiddleLeft, true);
            y += 40f;
            string name = s.PlayerName;
            TextField(new Rect(x, y, w, 56f), ref name, 16);
            if (name != s.PlayerName)
            {
                s.PlayerName = name;
                _game.ProfileChanged(false);
            }
            y += 76f;

            T(new Rect(x, y, 300f, 40f), "FORMA DA CABEÇA", 24, Ivory, TextAnchor.MiddleLeft, true);
            y += 44f;
            string[] shapes = { "TRIÂNGULO", "CÍRCULO", "QUADRADO" };
            float tw = (w - 24f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var tile = new Rect(x + i * (tw + 12f), y, tw, 150f);
                bool on = s.Archetype == i;
                Round(tile, Darker(on ? Mustard : NavyDark, 0.6f), 14f);
                Round(Inset(tile, 3f), on ? Color.Lerp(NavyDark, Mustard, 0.25f) : NavyDark, 12f);
                if (on) Outline(tile, Mustard, 4f, 14f);
                Shape(new Rect(tile.center.x - 42f, tile.y + 18f, 84f, 84f), (Archetype)i, ArtPalette.Heads[Mathf.Clamp(s.ColorIndex, 0, ArtPalette.Heads.Length - 1)]);
                T(new Rect(tile.x, tile.yMax - 44f, tile.width, 36f), shapes[i], 20, on ? Mustard : Ivory, TextAnchor.MiddleCenter, true);
                if (Interactive && GUI.Button(tile, GUIContent.none, GUIStyle.none) && s.Archetype != i)
                {
                    s.Archetype = i;
                    Changed();
                }
            }
            y += 170f;

            T(new Rect(x, y, 300f, 40f), "COR", 24, Ivory, TextAnchor.MiddleLeft, true);
            y += 44f;
            float sw = Mathf.Min(64f, (w - 7 * 10f) / 8f);
            for (int i = 0; i < ArtPalette.Heads.Length; i++)
            {
                var r = new Rect(x + i * (sw + 10f), y, sw, sw);
                if (s.ColorIndex == i) Round(new Rect(r.x - 5f, r.y - 5f, r.width + 10f, r.height + 10f), Ivory, (sw + 10f) * 0.5f);
                Round(r, ArtPalette.Heads[i], sw * 0.5f);
                if (Interactive && GUI.Button(r, GUIContent.none, GUIStyle.none) && s.ColorIndex != i)
                {
                    s.ColorIndex = i;
                    Changed();
                }
            }
            y += sw + 30f;

            T(new Rect(x, y, 200f, 56f), "BONÉ", 24, Ivory, TextAnchor.MiddleLeft, true);
            int hat = Cycler(new Rect(x + 190f, y, w - 190f, 56f), CosmeticIds.Hats[s.HatId], s.HatId, CosmeticIds.HatCount);
            if (hat != s.HatId)
            {
                s.HatId = hat;
                Changed();
            }
            y += 72f;
            T(new Rect(x, y, 200f, 56f), "ACESSÓRIO", 24, Ivory, TextAnchor.MiddleLeft, true);
            int acc = Cycler(new Rect(x + 190f, y, w - 190f, 56f), CosmeticIds.Accessories[s.AccessoryId], s.AccessoryId, CosmeticIds.AccessoryCount);
            if (acc != s.AccessoryId)
            {
                s.AccessoryId = acc;
                Changed();
            }
            y += 80f;

            T(new Rect(x, y, w, 60f), "Só visual: a forma, a cor e os acessórios não mudam hitbox, física, moedas nem vidas.", 18, Muted, TextAnchor.UpperLeft, false, false, true);

            if (Primary(new Rect(x, panel.yMax - 100f, w, 76f), fromMenu ? "GUARDAR E VOLTAR" : "FECHAR", true, 30))
            {
                _game.Settings.Save();
                _game.ProfileChanged(true);
                if (fromMenu) _page = MenuPage.Main;
                else _customizeOverlay = false;
            }
        }

        void Changed()
        {
            _celebrateUntil = Time.unscaledTime + 1.6f;
            _game.ProfileChanged(true);
        }

        // ------------------------------------------------------------------ a ligar (concept 33)

        void DrawConnecting()
        {
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.72f));
            float cx = _vw * 0.5f, cy = _vh * 0.45f;
            Logo(new Rect(cx - 130f, cy - 250f, 260f, 260f));
            // Bolas a rodar à volta do logótipo.
            for (int i = 0; i < 8; i++)
            {
                float a = Time.unscaledTime * 2.4f + i * Mathf.PI * 0.25f;
                float rad = 170f;
                var c = VisualFactory.BallColor(i + 1);
                float size = 18f + 8f * Mathf.Max(0f, Mathf.Sin(Time.unscaledTime * 4f - i * 0.6f));
                Round(new Rect(cx + Mathf.Cos(a) * rad - size * 0.5f, cy - 120f + Mathf.Sin(a) * rad - size * 0.5f, size, size), c, size * 0.5f);
            }
            T(new Rect(0f, cy + 70f, _vw, 80f), "A LIGAR…", 64, Mustard, TextAnchor.MiddleCenter, true, true);
            T(new Rect(0f, cy + 150f, _vw, 40f), _game.StatusMessage ?? "", 24, Ivory, TextAnchor.MiddleCenter);
            T(new Rect(0f, cy + 196f, _vw, 34f), Tip(), 20, Muted, TextAnchor.MiddleCenter);
            if (Secondary(new Rect(cx - 150f, cy + 250f, 300f, 76f), "CANCELAR", true, 28)) _game.Leave();
        }

        static readonly string[] Tips =
        {
            "Dica: a loja de sabotagem é uma máquina física — vai até ela e carrega em [E].",
            "Dica: cada bola legal encaçapada dá +10 moedas.",
            "Dica: quem vai atrás no marcador tem desconto na sabotagem.",
            "Dica: Caos custa o dobro, mas o efeito é muito mais forte.",
            "Dica: a jukebox aceita pedidos de todos — e vota-se para saltar.",
        };

        static string Tip() => Tips[(int)(Time.unscaledTime / 5f) % Tips.Length];
    }
}
