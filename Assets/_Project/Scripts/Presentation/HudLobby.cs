using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Sala de espera (concept 07): código da sala com copiar/convidar, placas dos jogadores (pronto / anfitrião),
    /// barra inferior SAIR · DEFINIÇÕES · PRONTO · COMEÇAR e o painel de regras do anfitrião
    /// (jogadores, bolas, rondas, revólver, economia, bots e expulsar).
    /// </summary>
    public sealed partial class HudView
    {
        bool _roomSettingsOpen;
        // Painel da sala: 0 regras da partida, 1 sala (permissões, reinício). Confirmação do reinício (tempo do pedido).
        int _roomTab;
        float _resetConfirmUntil;
        float _copiedAt = -10f;

        void DrawLobby(NetGame net)
        {
            var view = net.View;
            bool host = net.IsLocalHost;

            DrawCodePlate(net);
            DrawSlotPlates(net, 200f);

            // Barra inferior.
            float barH = 120f;
            var bar = new Rect(30f, _vh - barH - 20f, _vw - 60f, barH);
            Plate(bar, NavyDark);
            float gap = 18f, bw = (bar.width - 60f - gap * 4f) / 5f, bh = 80f, by = bar.y + (barH - bh) * 0.5f, bx = bar.x + 30f;

            if (DangerBtn(new Rect(bx, by, bw, bh), "SAIR DA SALA", true, 26, IconKind.Door)) _game.Leave();
            bx += bw + gap;
            if (Secondary(new Rect(bx, by, bw, bh), host ? "REGRAS DA SALA" : "VER REGRAS", true, 26, IconKind.Gear)) _roomSettingsOpen = !_roomSettingsOpen;
            bx += bw + gap;
            if (Secondary(new Rect(bx, by, bw, bh), "PERSONALIZAR", true, 26, IconKind.Head)) _customizeOverlay = !_customizeOverlay;
            bx += bw + gap;

            int ready = 0, humans = 0;
            foreach (var s in view.Slots)
            {
                if (s.Profile.IsBot) continue;
                humans++;
                if (s.Ready || s.ClientId == view.HostClientId) ready++;
            }
            if (host)
            {
                SuccessBtn(new Rect(bx, by, bw, bh), "ANFITRIÃO", false, 26, IconKind.Check);
            }
            else
            {
                var mine = net.LocalSlot;
                bool isReady = mine != null && mine.Ready;
                if (isReady ? SuccessBtn(new Rect(bx, by, bw, bh), "PRONTO", true, 30, IconKind.Check) : Secondary(new Rect(bx, by, bw, bh), "FICAR PRONTO (R)", mine != null, 24, IconKind.Check))
                    net.SetReady(!isReady);
            }
            bx += bw + gap;

            int min = Mathf.Max(2, NetGame.SharedConfig != null ? NetGame.SharedConfig.PlayerMin : 2);
            bool enough = view.Slots.Count >= min;
            if (host)
            {
                string label = enough ? "COMEÇAR" : "FALTAM " + (min - view.Slots.Count);
                if (Primary(new Rect(bx, by, bw, bh), label, enough, 30, IconKind.Play)) net.HostStartMatch();
            }
            else
            {
                Btn(new Rect(bx, by, bw, bh), "À ESPERA DO ANFITRIÃO", NavyLight, Ivory, false, 20);
            }

            // Estado e dicas por cima da barra.
            string status = ready + "/" + humans + " prontos";
            if (!enough) status += view.Slots.Count <= 1
                ? "  ·  falta um adversário: " + (host ? "adiciona um bot nas regras ou espera por um amigo" : "à espera de mais alguém")
                : "  ·  precisas de pelo menos " + min + " participantes" + (host ? " (adiciona bots nas regras)" : "");
            T(new Rect(40f, bar.y - 44f, _vw - 80f, 36f), status, 22, enough ? Ivory : Mustard, TextAnchor.MiddleLeft, true, true);
            string hint = _game.CursorFree ? "CLICA NO CENÁRIO PARA ANDAR  ·  R — PRONTO" : "TAB — RATO PARA OS BOTÕES  ·  R — PRONTO";
            Pill(new Rect(_vw - 600f, bar.y - 48f, 560f, 40f), hint, Mustard, NavyDark, 18);

        }

        void DrawCodePlate(NetGame net)
        {
            var view = net.View;
            string label, code;
            switch (view.Kind)
            {
                case SessionKind.Online:
                    label = "CÓDIGO DA SALA";
                    code = view.JoinCode;
                    break;
                case SessionKind.Direct:
                    label = "ENDEREÇO (REDE LOCAL)";
                    code = view.JoinCode;
                    break;
                default:
                    label = "TREINO LOCAL";
                    code = "OFFLINE";
                    break;
            }

            float w = Mathf.Min(1000f, _vw - 80f), x = (_vw - w) * 0.5f;
            var r = new Rect(x, 22f, w, 150f);
            Plate(r, NavyDark);
            Logo(new Rect(r.x + 18f, r.y + 14f, 122f, 122f));
            Fill(new Rect(r.x + 158f, r.y + 28f, 3f, r.height - 56f), NavyLight);
            T(new Rect(r.x + 180f, r.y + 18f, 420f, 34f), label, 22, Muted, TextAnchor.MiddleLeft, true);
            int size = code.Length > 12 ? 44 : 70;
            T(new Rect(r.x + 180f, r.y + 48f, 460f, 80f), code, size, Mustard, TextAnchor.MiddleLeft, true, true);
            string room = (string.IsNullOrEmpty(view.RoomName) ? "Sala" : view.RoomName) + (view.Kind == SessionKind.Offline ? "" : view.IsPrivate ? "  ·  PRIVADA" : "  ·  PÚBLICA");
            T(new Rect(r.x + 180f, r.y + 118f, 460f, 26f), room, 18, Ivory, TextAnchor.MiddleLeft, true);

            if (view.Kind == SessionKind.Offline) return;
            var copy = new Rect(r.x + 650f, r.y + 42f, 72f, 72f);
            if (Btn(copy, "", Navy, Ivory, true)) Copy(code);
            Icon(Inset(copy, 20f), IconKind.Copy, Ivory);
            string invite = view.Kind == SessionKind.Online
                ? "Bora jogar Caçapa Russa! Multiplayer ▸ código da sala: " + code
                : "Bora jogar Caçapa Russa! Multiplayer ▸ IP: " + code;
            if (Primary(new Rect(r.xMax - 260f, r.y + 38f, 236f, 80f), Time.unscaledTime - _copiedAt < 1.6f ? "COPIADO!" : "CONVIDAR", true, 30, IconKind.Plus)) Copy(invite);
        }

        void Copy(string text)
        {
            GUIUtility.systemCopyBuffer = text;
            _copiedAt = Time.unscaledTime;
            ShowToast("Copiado para a área de transferência — envia aos teus amigos.", 2.5f);
        }

        void DrawSlotPlates(NetGame net, float y)
        {
            var view = net.View;
            int n = view.Slots.Count;
            if (n == 0) return;
            float gap = 14f;
            float w = Mathf.Min(290f, (_vw - 60f - gap * (n - 1)) / n);
            float x = (_vw - (w * n + gap * (n - 1))) * 0.5f;
            foreach (var slot in view.Slots)
            {
                var r = new Rect(x, y, w, 96f);
                bool isHost = !slot.Profile.IsBot && slot.ClientId == view.HostClientId;
                bool me = !slot.Profile.IsBot && slot.ClientId == net.LocalClientId;
                Plate(r, NavyDark, false, 18f, me);
                HeadIcon(new Rect(r.x + 12f, r.y + 12f, 72f, 72f), slot.Profile, !slot.Connected);
                float tx = r.x + 96f, tw = r.width - 106f;
                T(new Rect(tx, r.y + 10f, tw, 36f), slot.Profile.Name, 26, Ivory, TextAnchor.MiddleLeft, true);
                if (isHost)
                {
                    Crown(new Rect(Mathf.Min(tx + Style(26, TextAnchor.MiddleLeft, true).CalcSize(new GUIContent(slot.Profile.Name)).x + 8f, r.xMax - 36f), r.y + 16f, 28f, 22f));
                    T(new Rect(tx, r.y + 42f, tw, 20f), "ANFITRIÃO", 15, Mustard, TextAnchor.MiddleLeft, true);
                }
                string state;
                Color dot;
                if (slot.Profile.IsBot) { state = "BOT"; dot = Teal; }
                else if (!slot.Connected) { state = "DESLIGADO"; dot = Coral; }
                else if (isHost || slot.Ready) { state = "PRONTO"; dot = GoodGreen; }
                else { state = "NÃO PRONTO"; dot = Coral; }
                if (r.width > 200f) CareerWins(new Rect(r.xMax - 64f, r.y + 64f, 56f, 24f), slot.Profile.Wins, 18, Mustard);
                Round(new Rect(tx, r.y + 68f, 16f, 16f), dot, 8f);
                T(new Rect(tx + 24f, r.y + 62f, tw - 24f, 28f), state, 20, dot, TextAnchor.MiddleLeft, true);
                x += w + gap;
            }
        }

        /// <summary>Balas no revólver ronda a ronda com estas regras (ex.: "1·1·2·3·4·5").</summary>
        static string BulletCurve(LobbySettings s)
        {
            var sb = new System.Text.StringBuilder();
            int rounds = 5;
            var risk = new RiskBag(s.RiskDanger, s.RiskTotal, null, s.RiskGrowthPct / 100f);
            for (int r = 1; r <= rounds; r++)
            {
                if (r > 1) sb.Append('·');
                sb.Append(Mathf.RoundToInt(100f * risk.ChanceAfterSurvivals(r - 1))).Append("%");
            }
            return sb.ToString();
        }

        void Crown(Rect r)
        {
            var old = GUI.color;
            GUI.color = Mustard;
            float tw = r.width * 0.42f;
            GUI.DrawTexture(new Rect(r.x, r.y, tw, r.height * 0.7f), _triUp);
            GUI.DrawTexture(new Rect(r.center.x - tw * 0.5f, r.y - r.height * 0.1f, tw, r.height * 0.8f), _triUp);
            GUI.DrawTexture(new Rect(r.xMax - tw, r.y, tw, r.height * 0.7f), _triUp);
            GUI.color = old;
            Round(new Rect(r.x, r.y + r.height * 0.55f, r.width, r.height * 0.45f), Mustard, 3f);
        }

        // ------------------------------------------------------------------ regras da sala

        void DrawRoomSettings(NetGame net)
        {
            var view = net.View;
            bool host = net.IsLocalHost;
            var s = view.Settings;
            var e = s.Copy();

            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0f, 0f, 0f, 0.5f));
            float w = Mathf.Min(1480f, _vw - 60f), h = Mathf.Min(820f, _vh - 60f), x = (_vw - w) * 0.5f, y = (_vh - h) * 0.5f;
            var panel = new Rect(x, y, w, h);
            Plate(panel, Mustard, true, 20f);
            var inner = Inset(panel, 14f);
            Round(inner, NavyDark, 14f);
            T(new Rect(inner.x + 30f, inner.y + 10f, 700f, 70f), host ? (view.InMatch ? "PAINEL DO ANFITRIÃO" : "REGRAS DA SALA") : "REGRAS DA SALA (SÓ O ANFITRIÃO ALTERA)", host ? 48 : 36, Mustard, TextAnchor.MiddleLeft, true);
            float tabW = 220f;
            if (Btn(new Rect(inner.xMax - 30f - tabW * 2f - 10f, inner.y + 18f, tabW, 54f), "REGRAS", _roomTab == 0 ? Mustard : Navy, _roomTab == 0 ? NavyDark : Ivory, true, 22)) _roomTab = 0;
            if (Btn(new Rect(inner.xMax - 30f - tabW, inner.y + 18f, tabW, 54f), "SALA", _roomTab == 1 ? Mustard : Navy, _roomTab == 1 ? NavyDark : Ivory, true, 22)) _roomTab = 1;
            if (_roomTab == 1)
            {
                DrawRoomHostTab(net, inner, s, e);
                if (Primary(new Rect(inner.center.x - 160f, inner.yMax - 84f, 320f, 70f), "FECHAR", true, 30)) _roomSettingsOpen = false;
                return;
            }
            // A meio da partida, as regras alteradas entram em vigor no início da próxima ronda (a actual não muda).
            if (view.InMatch) T(new Rect(inner.x + 30f, inner.y + 66f, 900f, 28f), "As alterações às regras valem a partir da próxima ronda.", 18, Mustard, TextAnchor.MiddleLeft, true);

            GUI.enabled = host;
            float colW = (inner.width - 90f) / 2f;
            float lx = inner.x + 30f, rx = lx + colW + 30f, ly = inner.y + 96f, ry = ly;
            float cw = 300f;

            Row(lx, ref ly, colW, "MÁX. JOGADORES", () => e.MaxPlayers = Stepper(CtlRect(lx, ly, colW, cw), s.MaxPlayers, 2, 6, 1));
            Row(lx, ref ly, colW, "VITÓRIA", () => Pill(CtlRect(lx, ly, colW, cw), "Último sobrevivente", Navy, Ivory, 20));
            Row(lx, ref ly, colW, "BOLAS NA MESA", () => e.BallCount = BallStepper(CtlRect(lx, ly, colW, cw), s.BallCount));
            int[] growths = { 110, 125, 140, 170 };
            string[] growthNames = { "Gradual", "Suave", "Exponencial", "Brutal" };
            int growthMode = 0;
            for (int i = 0; i < growths.Length; i++)
                if (s.RiskGrowthPct >= growths[i]) growthMode = i;
            Row(lx, ref ly, colW, "RISCO  " + BulletCurve(s), () =>
            {
                int next = Cycler(CtlRect(lx, ly, colW, cw), growthNames[growthMode], growthMode, growths.Length);
                if (next != growthMode) e.RiskGrowthPct = growths[next];
            });
            string[] racks = { "Adaptativo", "Rack de 9", "Rack de 6", "Rack de 4" };
            Row(lx, ref ly, colW, "RACK (SE BOLAS=AUTO)", () => e.RackMode = (RackMode)Cycler(CtlRect(lx, ly, colW, cw), racks[(int)s.RackMode], (int)s.RackMode, 4));
            int riskMode = s.RiskDanger == 2 ? 2 : s.RiskTotal >= 8 ? 0 : 1;
            string[] risks = { "Casual 1 em 8", "Normal 1 em 6", "Caos 2 em 7" };
            Row(lx, ref ly, colW, "REVÓLVER", () =>
            {
                int next = Cycler(CtlRect(lx, ly, colW, cw), risks[riskMode], riskMode, 3);
                if (next == riskMode) return;
                e.RiskDanger = next == 2 ? 2 : 1;
                e.RiskTotal = next == 0 ? 8 : next == 1 ? 6 : 7;
            });
            Row(lx, ref ly, colW, "DEBUFFS", () => e.EnableDebuffs = Switch(new Rect(lx + colW - 116f, ly + 9f, 96f, 40f), s.EnableDebuffs));
            Row(lx, ref ly, colW, "SABOTAGEM", () => e.EnableSabotage = Switch(new Rect(lx + colW - 116f, ly + 9f, 96f, 40f), s.EnableSabotage));

            Row(rx, ref ry, colW, "PRÉMIO VENCEDOR", () => e.WinnerReward = Stepper(CtlRect(rx, ry, colW, cw), s.WinnerReward, 0, 300, 25));
            Row(rx, ref ry, colW, "PRÉMIO PARTICIPAÇÃO", () => e.ParticipationReward = Stepper(CtlRect(rx, ry, colW, cw), s.ParticipationReward, 0, 200, 10));
            Row(rx, ref ry, colW, "CUSTO TRAVESSURA", () => e.SabotageCost = Stepper(CtlRect(rx, ry, colW, cw), s.SabotageCost, 20, 300, 10, s.SabotageCost + " (Caos " + Mathf.CeilToInt(s.SabotageCost * (NetGame.SharedConfig != null ? NetGame.SharedConfig.ChaosCostMultiplier : 2f)) + ")"));
            Row(rx, ref ry, colW, "SALDO MÁXIMO", () => e.CoinCap = Stepper(CtlRect(rx, ry, colW, cw), s.CoinCap, 100, 1000, 50));
            Row(rx, ref ry, colW, "RECARGA DA MÁQUINA", () => e.SabotageCooldown = Stepper(CtlRect(rx, ry, colW, cw), s.SabotageCooldown, 5, 90, 5, s.SabotageCooldown + " s"));
            Row(rx, ref ry, colW, "TEMPO POR TACADA", () => e.TurnSeconds = Stepper(CtlRect(rx, ry, colW, cw), s.TurnSeconds, 0, 120, 5, s.TurnSeconds == 0 ? "∞" : s.TurnSeconds + " s"));
            Row(rx, ref ry, colW, "DIFICULDADE DOS BOTS", () =>
            {
                e.BotDifficulty = Cycler(CtlRect(rx, ry, colW, cw), BotLevels[Mathf.Clamp(s.BotDifficulty, 0, 2)], Mathf.Clamp(s.BotDifficulty, 0, 2), 3);
                if (host && e.BotDifficulty != s.BotDifficulty)
                {
                    _game.Settings.BotDifficulty = e.BotDifficulty;
                    _game.Settings.Save();
                }
            });
            GUI.enabled = true;

            if (host && !e.SameAs(s)) net.HostApplySettings(e);

            // Jogadores: bots e expulsar.
            ry += 6f;
            T(new Rect(rx, ry, 300f, 40f), "JOGADORES " + view.Slots.Count + "/" + s.MaxPlayers, 26, Ivory, TextAnchor.MiddleLeft, true);
            if (host)
            {
                // Bots só se juntam/saem na sala de espera (o anfitrião recusa a meio da partida).
                if (Secondary(new Rect(rx + colW - 300f, ry - 4f, 140f, 50f), "+ BOT", !view.InMatch && view.Slots.Count < s.MaxPlayers, 20)) net.HostSetBots(true);
                if (Secondary(new Rect(rx + colW - 150f, ry - 4f, 140f, 50f), "− BOT", !view.InMatch && HasBot(view), 20)) net.HostSetBots(false);
            }
            ry += 54f;
            float kickY = ry;
            foreach (var slot in view.Slots)
            {
                if (kickY > inner.yMax - 60f) break;
                if (slot.Profile.IsBot || slot.ClientId == view.HostClientId) continue;
                HeadIcon(new Rect(rx, kickY, 40f, 40f), slot.Profile, !slot.Connected, false);
                T(new Rect(rx + 52f, kickY, colW - 260f, 40f), slot.Profile.Name, 22, Ivory, TextAnchor.MiddleLeft, true);
                if (host && DangerBtn(new Rect(rx + colW - 170f, kickY - 2f, 160f, 44f), "EXPULSAR", true, 18)) net.HostKick(slot.ClientId);
                kickY += 50f;
            }

            if (Primary(new Rect(inner.center.x - 160f, inner.yMax - 84f, 320f, 70f), "FECHAR", true, 30)) _roomSettingsOpen = false;
        }

        /// <summary>
        /// Separador "SALA": o que o anfitrião controla sem recriar a sala — subir à mesa, permissões da jukebox
        /// (adicionar e pular, independentes) e reiniciar a partida (com confirmação). Os outros vêem o estado.
        /// </summary>
        void DrawRoomHostTab(NetGame net, Rect inner, LobbySettings s, LobbySettings e)
        {
            var view = net.View;
            bool host = net.IsLocalHost;
            var music = view.Music.Online;
            float colW = (inner.width - 90f) / 2f;
            float lx = inner.x + 30f, ly = inner.y + 96f, rx = lx + colW + 30f, ry = ly;

            T(new Rect(lx, ly, colW, 36f), "JOGADORES", 24, Mustard, TextAnchor.MiddleLeft, true);
            ly += 44f;
            GUI.enabled = host;
            Row(lx, ref ly, colW, "SUBIR À MESA", () => e.AllowTableClimb = Switch(new Rect(lx + colW - 116f, ly + 9f, 96f, 40f), s.AllowTableClimb));
            T(new Rect(lx + 4f, ly - 12f, colW, 24f), s.AllowTableClimb ? "Podem saltar para cima da mesa. Objectos atirados passam sempre." : "Ninguém sobe à mesa. Objectos atirados continuam a poder cair nela.", 15, Muted, TextAnchor.MiddleLeft);
            ly += 20f;
            if (host && !e.SameAs(s)) net.HostApplySettings(e);

            T(new Rect(lx, ly, colW, 36f), "JUKEBOX", 24, Mustard, TextAnchor.MiddleLeft, true);
            ly += 44f;
            Row(lx, ref ly, colW, "ADICIONAR MÚSICAS", () =>
            {
                if (Switch(new Rect(lx + colW - 116f, ly + 9f, 96f, 40f), !music.Locked) == music.Locked) net.ControlOnlineMusic(OnlineMusicCommand.Lock);
            });
            Row(lx, ref ly, colW, "PULAR MÚSICA (TODOS)", () =>
            {
                if (Switch(new Rect(lx + colW - 116f, ly + 9f, 96f, 40f), !music.SkipLocked) == music.SkipLocked) net.ControlOnlineMusic(OnlineMusicCommand.SkipLock);
            });
            T(new Rect(lx + 4f, ly - 12f, colW, 24f), music.SkipLocked ? "Só tu passas à música seguinte; os outros vêem o botão bloqueado." : "Qualquer jogador pode pular (na rádio, por votação).", 15, Muted, TextAnchor.MiddleLeft);
            GUI.enabled = true;

            T(new Rect(rx, ry, colW, 36f), "PARTIDA", 24, Mustard, TextAnchor.MiddleLeft, true);
            ry += 44f;
            if (!view.InMatch)
            {
                T(new Rect(rx, ry, colW, 60f), "Sem partida a decorrer. As regras ficam no separador REGRAS.", 19, Muted, TextAnchor.UpperLeft, false, false, true);
                return;
            }
            T(new Rect(rx, ry, colW, 56f), "Ronda " + view.RoundIndex + " · " + view.ActiveCount + " em jogo", 22, Ivory, TextAnchor.MiddleLeft, true);
            ry += 64f;
            if (!host) return;
            bool confirming = Time.unscaledTime < _resetConfirmUntil;
            if (!confirming)
            {
                if (DangerBtn(new Rect(rx, ry, colW, 64f), "REINICIAR PARTIDA", true, 24)) _resetConfirmUntil = Time.unscaledTime + 6f;
                T(new Rect(rx + 4f, ry + 70f, colW, 48f), "Volta à ronda 1 com os mesmos jogadores: saldos, efeitos e roleta recomeçam do zero.", 16, Muted, TextAnchor.UpperLeft, false, false, true);
            }
            else
            {
                T(new Rect(rx, ry - 4f, colW, 30f), "Tens a certeza? A partida actual perde-se.", 20, Coral, TextAnchor.MiddleLeft, true);
                float half = (colW - 12f) * 0.5f;
                if (DangerBtn(new Rect(rx, ry + 32f, half, 60f), "SIM, REINICIAR", true, 22))
                {
                    _resetConfirmUntil = 0f;
                    net.HostResetMatch();
                    _roomSettingsOpen = false;
                }
                if (Secondary(new Rect(rx + half + 12f, ry + 32f, half, 60f), "CANCELAR", true, 22)) _resetConfirmUntil = 0f;
            }
        }

        static bool HasBot(MatchView view)
        {
            foreach (var s in view.Slots)
                if (s.Profile.IsBot) return true;
            return false;
        }

        static Rect CtlRect(float x, float y, float colW, float cw) => new Rect(x + colW - cw - 16f, y + 9f, cw, 42f);

        void Row(float x, ref float y, float w, string label, System.Action control)
        {
            SettingRow(x, y, w, label);
            control();
            y += 68f;
        }
    }
}
