using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// HUD de sessão/partida (concepts 08–14, 31, 32): placa do jogador, ordem de jogo, moedas e debuffs,
    /// marcador, cartões de fase, gatilho (clique esquerdo), resultado do debuff, loja desenhada no ecrã da
    /// máquina de sabotagem, jukebox, ecrã de eliminação e vitória.
    /// </summary>
    public sealed partial class HudView
    {
        readonly List<ModifierDefinition> _tierScratch = new List<ModifierDefinition>();

        void DrawSession()
        {
            var net = NetGame.Instance;
            if (net == null || !net.HasSnapshot)
            {
                T(new Rect(0f, _vh * 0.45f, _vw, 60f), "A SINCRONIZAR COM O ANFITRIÃO…", 40, Muted, TextAnchor.MiddleCenter, true, true);
                return;
            }
            var view = net.View;
            _rowRects.Clear();
            bool outerBlock = _blockInput;
            bool modal = _roomSettingsOpen || _customizeOverlay || (_eliminatedOverlay && view.InMatch && view.Phase != MatchPhase.MatchOver);
            if (modal) _blockInput = true;

            DrawNameplates(net);
            DrawCrosshair();

            if (_game.Interaction.Active is TvStation)
            {
                // "Ver na TV": o ecrã fica limpo; na partida mantém-se só de quem é a vez.
                if (view.InMatch && view.Phase != MatchPhase.MatchOver) { DrawTurnOrder(net); DrawPhase(net); }
            }
            else if (!view.InMatch)
            {
                DrawLobby(net);
            }
            else if (view.Phase == MatchPhase.MatchOver)
            {
                if (ResultsOpen) DrawResults(net);
                else DrawFunMode(net);
            }
            else
            {
                DrawPlayerBadge(net);
                DrawTurnOrder(net);
                DrawWallet(net);
                DrawScoreboard(net);
                DrawPhase(net);
                DrawShotPrompts(net);
                DrawSpectatorBar(net);
            }

            var active = _game.Interaction.Active;
            if (active is SabotageMachine machine) DrawMachineScreen(net, machine);
            else if (active is JukeboxStation || active is DvdStation) DrawJukebox(net);
            else if (active is TvStation tv) DrawTvControls(net, tv);
            else DrawInteractionPrompt();

            if (!ResultsOpen) DrawPings();
            DrawFeed();
            DrawFloaters();
            if (view.InMatch && view.Phase != MatchPhase.MatchOver) DrawScores(_walletRect);
            DrawBanner();
            DrawJukeboxPrompt(net);
            DrawImpactFrame();
            _blockInput = outerBlock;
            if (_roomSettingsOpen) DrawRoomSettings(net);
            if (_customizeOverlay) DrawCustomize(new Rect(40f, 40f, 640f, _vh - 80f), false);
            if (_eliminatedOverlay && view.InMatch && view.Phase != MatchPhase.MatchOver) DrawEliminated(net);
            if (_game.Settings.ShowHelp && active == null && !_roomSettingsOpen) DrawHelp(net);
            DrawChat(net);
        }

        // ------------------------------------------------------------------ mundo

        void DrawCrosshair()
        {
            if (_game.CursorFree || _game.Shot.UsesShotCamera || _game.Interaction.Active != null || _game.Spectator.ControlsCamera) return;
            float cx = _vw * 0.5f, cy = _vh * 0.5f;
            bool focus = _game.Interaction.Focused != null;
            float s = focus ? 18f : 8f;
            if (focus) Outline(new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s), Mustard, 3f, s * 0.5f);
            Round(new Rect(cx - 4f, cy - 4f, 8f, 8f), new Color(0f, 0f, 0f, 0.5f), 4f);
            Round(new Rect(cx - 2.5f, cy - 2.5f, 5f, 5f), Ivory, 2.5f);
        }

        void DrawInteractionPrompt()
        {
            var focused = _game.Interaction.Focused;
            if (focused == null || _game.CursorFree) return;
            float w = 640f;
            var r = new Rect((_vw - w) * 0.5f, _vh * 0.5f + 60f, w, 56f);
            Round(r, new Color(0.04f, 0.06f, 0.1f, 0.82f), 28f);
            Outline(r, Mustard, 2f, 28f);
            T(r, focused.Prompt, 24, Ivory, TextAnchor.MiddleCenter);
        }

        void DrawNameplates(NetGame net)
        {
            var cam = _game.Rig.Camera;
            if (cam == null) return;
            var view = net.View;
            if (view.InMatch)
            {
                foreach (var p in view.Players)
                {
                    if (!p.IsBot && p.ClientId == net.LocalClientId) continue;
                    var avatar = AvatarLocator.Find(net, p.Id);
                    if (avatar == null || avatar.Head == null) continue;
                    bool over = view.Phase == MatchPhase.MatchOver;
                    string status = p.Eliminated && !over ? "FANTASMA" : !p.Connected ? "DESLIGADO" : over && p.Id == view.MatchWinner ? "VENCEDOR" : "";
                    DrawPlate(cam, avatar.Head.position, p.Profile, status, -1, p.Mods.Count, p.IsBot ? ulong.MaxValue - 1 : p.ClientId);
                }
            }
            else
            {
                foreach (var slot in view.Slots)
                {
                    if (!slot.Profile.IsBot && slot.ClientId == net.LocalClientId) continue;
                    AvatarVisual avatar = slot.Profile.IsBot
                        ? (NpcAvatars.Instance != null ? NpcAvatars.Instance.ForSlot(slot.SlotId) : null)
                        : NetPlayer.ForClient(slot.ClientId)?.Visual;
                    if (avatar == null || avatar.Head == null) continue;
                    string status = slot.Profile.IsBot ? "BOT" : slot.ClientId == view.HostClientId ? "ANFITRIÃO" : slot.Ready ? "PRONTO" : "";
                    DrawPlate(cam, avatar.Head.position, slot.Profile, status, -1, 0, slot.ClientId);
                }
            }
        }

        void DrawPlate(Camera cam, Vector3 head, PlayerProfile profile, string status, int lives, int debuffs, ulong clientId)
        {
            Vector3 sp = cam.WorldToScreenPoint(head + Vector3.up * 0.5f);
            if (sp.z <= 0f || sp.z > 16f) return;
            var pos = new Vector2(sp.x / _scale, (UnityEngine.Screen.height - sp.y) / _scale);
            float k = Mathf.Clamp(1.25f - sp.z * 0.06f, 0.6f, 1.1f);
            float w = 210f * k, h = 40f * k;
            var r = new Rect(pos.x - w * 0.5f, pos.y - h, w, h);
            Round(r, new Color(0.04f, 0.06f, 0.1f, 0.78f), h * 0.5f);
            Round(new Rect(r.x + 6f * k, r.y + 6f * k, h - 12f * k, h - 12f * k), profile.Color, (h - 12f * k) * 0.5f);
            T(new Rect(r.x + h, r.y, w - h - 10f * k, h), profile.Name, Mathf.RoundToInt(22f * k), Ivory, TextAnchor.MiddleLeft, true);
            float below = r.yMax + 4f;
            if (lives >= 0)
            {
                float pip = 11f * k;
                Pips(pos.x - (lives * (pip + 6f)) * 0.5f, below, lives, lives, pip, Coral);
                below += pip + 4f;
            }
            if (debuffs > 0) T(new Rect(r.x, below, w, 20f), debuffs + (debuffs == 1 ? " DEBUFF" : " DEBUFFS"), Mathf.RoundToInt(15f * k), SabotageColor, TextAnchor.MiddleCenter, true);
            if (!string.IsNullOrEmpty(status)) T(new Rect(r.x, r.y - 22f * k, w, 20f * k), status, Mathf.RoundToInt(15f * k), status == "PRONTO" ? GoodGreen : Mustard, TextAnchor.MiddleCenter, true);

            if (_emotes.TryGetValue(clientId, out var bubble) && Time.unscaledTime - bubble.Time < 2.5f)
            {
                var b = new Rect(pos.x - 100f, r.y - 70f, 200f, 44f);
                Round(b, Ivory, 22f);
                T(b, bubble.Text, 24, NavyDark, TextAnchor.MiddleCenter, true);
            }
        }

        // ------------------------------------------------------------------ placas fixas (concept 08)

        void DrawPlayerBadge(NetGame net)
        {
            var v = net.View;
            var me = net.LocalPlayer;
            var r = new Rect(24f, 20f, 520f, 120f);
            Plate(r, NavyDark);
            var profile = me != null ? me.Profile : _game.LocalProfile;
            HeadIcon(new Rect(r.x + 14f, r.y + 12f, 96f, 96f), profile, me != null && me.Eliminated);
            T(new Rect(r.x + 126f, r.y + 14f, 250f, 50f), profile.Name, 38, Ivory, TextAnchor.MiddleLeft, true);
            if (me == null) T(new Rect(r.x + 126f, r.y + 66f, 250f, 36f), "ESPECTADOR", 22, Muted, TextAnchor.MiddleLeft, true);
            else if (me.Eliminated) T(new Rect(r.x + 126f, r.y + 66f, 250f, 36f), "FANTASMA", 22, SabotageColor, TextAnchor.MiddleLeft, true);
            else CareerWins(new Rect(r.x + 128f, r.y + 70f, 250f, 34f), profile.Wins, 24, Mustard);

            Fill(new Rect(r.x + 386f, r.y + 18f, 3f, r.height - 36f), NavyLight);
            T(new Rect(r.x + 396f, r.y + 14f, 118f, 34f), v.SuddenDeath ? "MORTE" : "RONDA", 22, Ivory, TextAnchor.MiddleCenter, true);
            T(new Rect(r.x + 396f, r.y + 46f, 118f, 60f), v.SuddenDeath ? "SÚBITA" : v.RoundIndex.ToString(), v.SuddenDeath ? 28 : 44, v.SuddenDeath ? Coral : Mustard, TextAnchor.MiddleCenter, true);
        }

        /// <summary>Tambor do revólver desta ronda: tambores com bala a verde-gosma, o resto são debuffs.</summary>
        void DrawRevolver(NetGame net, float y)
        {
            var v = net.View;
            int chambers = Mathf.Clamp(v.Chambers, 1, 12);
            int loaded = Mathf.Clamp(v.LoadedChambers, 0, chambers);
            var r = new Rect(24f, y, 300f, 96f);
            Plate(r, NavyDark, false, 22f);
            var drum = new Rect(r.x + 12f, r.y + 10f, 76f, 76f);
            Round(drum, Darker(Mustard, 0.55f), 38f);
            Round(Inset(drum, 4f), Darker(Mustard, 0.85f), 34f);
            float spin = Time.unscaledTime * (v.Phase == MatchPhase.PunishmentDraw ? 240f : 8f);
            for (int i = 0; i < chambers; i++)
            {
                float a = (spin + i * 360f / chambers) * Mathf.Deg2Rad;
                var c = new Vector2(drum.center.x + Mathf.Cos(a) * 24f, drum.center.y + Mathf.Sin(a) * 24f);
                var hole = new Rect(c.x - 9f, c.y - 9f, 18f, 18f);
                Round(hole, NavyDark, 9f);
                if (i < loaded) Round(Inset(hole, 3f), ArtPalette.Goo, 6f);
            }
            Round(new Rect(drum.center.x - 7f, drum.center.y - 7f, 14f, 14f), NavyDark, 7f);
            var riskPlayer = v.GetPlayer(v.Selected) ?? net.LocalPlayer;
            int pct = Mathf.RoundToInt(100f * (riskPlayer?.RiskChance ?? 1f / chambers));
            T(new Rect(r.x + 100f, r.y + 8f, 190f, 30f), "REVÓLVER", 18, Muted, TextAnchor.MiddleLeft, true);
            T(new Rect(r.x + 100f, r.y + 32f, 190f, 34f), "TENTATIVA " + ((riskPlayer?.RiskAttempts ?? 0) + 1), 26, loaded * 2 >= chambers ? Coral : Ivory, TextAnchor.MiddleLeft, true);
            T(new Rect(r.x + 100f, r.y + 62f, 190f, 26f), "morte " + Colored(pct + "%", ArtPalette.Goo) + " · risco individual", 15, Muted, TextAnchor.MiddleLeft, true);
        }

        void DrawTurnOrder(NetGame net)
        {
            var v = net.View;
            var order = v.RoundOrder.Count > 0 ? v.RoundOrder : v.BaseOrder;
            DrawRevolver(net, order.Count == 0 ? 152f : 244f);
            if (order.Count == 0) return;
            float size = 58f, gap = 30f;
            var r = new Rect(24f, 152f, 28f + order.Count * size + (order.Count - 1) * gap, 80f);
            Plate(r, NavyDark, false, 40f);
            float x = r.x + 14f;
            for (int i = 0; i < order.Count; i++)
            {
                var p = v.GetPlayer(order[i]);
                if (p == null) continue;
                bool current = p.Id == v.CurrentPlayer;
                var icon = new Rect(x, r.y + 11f, size, size);
                if (current) Round(new Rect(icon.x - 5f, icon.y - 5f, icon.width + 10f, icon.height + 10f), Mustard, (size + 10f) * 0.5f);
                HeadIcon(icon, p.Profile, p.Eliminated);
                if (i < order.Count - 1) Arrow(new Rect(x + size + 9f, r.center.y - 7f, 12f, 14f), true, Ivory);
                x += size + gap;
            }
        }

        void DrawWallet(NetGame net)
        {
            var v = net.View;
            var me = net.LocalPlayer;
            if (me == null) return;
            float x = _vw - 24f;
            var coins = new Rect(x - 220f, 20f, 220f, 84f);
            _walletRect = coins;
            float bump = 1f + _walletBump * 0.12f;
            Plate(coins, NavyDark, true, 20f);
            Coin(new Rect(coins.x + 26f - 24f * (bump - 1f), coins.y + 18f - 24f * (bump - 1f), 48f * bump, 48f * bump));
            T(new Rect(coins.x + 86f, coins.y, 120f, coins.height), WalletDisplay(me.Coins).ToString(), 44, Color.Lerp(Ivory, Mustard, _walletBump), TextAnchor.MiddleLeft, true);
            if (me.Coins >= v.CoinCap) T(new Rect(coins.x, coins.yMax + 2f, coins.width, 22f), "SALDO MÁXIMO", 15, Mustard, TextAnchor.MiddleCenter, true);
            else if (v.SabotageAnytime && me.IsActive)
            {
                int cheapest = v.PrankCost > 0 ? v.PrankCost : v.Settings.SabotageCost;
                bool ready = me.SabotageCooldown <= 0f && me.Coins >= cheapest;
                string label = me.SabotageCooldown > 0f ? "SABOTAGEM EM " + Mathf.CeilToInt(me.SabotageCooldown) + "s" : ready ? "SABOTAGEM PRONTA [E] NA MÁQUINA" : "FALTAM $" + (cheapest - me.Coins) + " PARA SABOTAR";
                T(new Rect(coins.x - 200f, coins.yMax + 2f, coins.width + 200f, 22f), label, 15, ready ? GoodGreen : Muted, TextAnchor.MiddleRight, true, true);
            }
        }

        Rect _walletRect;

        void DrawScoreboard(NetGame net)
        {
            var v = net.View;
            float w = 330f, x = _vw - w - 24f, y = 132f;
            // Muitos jogadores com a interface grande: linhas mais baixas para sobrar lugar às cartas de debuff.
            float rowH = v.BaseOrder.Count > 4 && _vh < 980f ? 58f : 70f;
            var panel = new Rect(x, y, w, 16f + v.BaseOrder.Count * (rowH + 8f));
            Plate(panel, NavyDark, false, 18f);
            float ry = y + 10f;
            foreach (int id in v.BaseOrder)
            {
                var p = v.GetPlayer(id);
                if (p == null) continue;
                bool current = id == v.CurrentPlayer && (v.Phase == MatchPhase.AwaitShot || v.Phase == MatchPhase.ShotWindup || v.Phase == MatchPhase.Simulating || v.Phase == MatchPhase.ShotResult);
                bool isMe = net.LocalPlayer != null && net.LocalPlayer.Id == id;
                var row = new Rect(x + 10f, ry, w - 20f, rowH);
                _rowRects[id] = row;
                Round(row, current ? Mustard : isMe ? NavyLight : Navy, 14f);
                float icon = rowH - 16f;
                HeadIcon(new Rect(row.x + 8f, row.y + 8f, icon, icon), p.Profile, p.Eliminated);
                Color text = current ? NavyDark : p.Eliminated ? Muted : Ivory;
                float sub = rowH < 70f ? 30f : 38f;
                T(new Rect(row.x + 72f, row.y + (rowH < 70f ? 2f : 6f), 170f, 34f), p.Name, rowH < 70f ? 21 : 24, text, TextAnchor.MiddleLeft, true);
                if (p.Eliminated) T(new Rect(row.x + 72f, row.y + sub, 170f, 24f), "FORA", 17, current ? NavyDark : Coral, TextAnchor.MiddleLeft, true);
                else
                {
                    float wx = CareerWins(new Rect(row.x + 72f, row.y + sub, 70f, 24f), p.Profile.Wins, 17, current ? NavyDark : Mustard);
                    // Risco pessoal da roleta (sobe só com as sobrevivências deste jogador).
                    T(new Rect(wx + 10f, row.y + sub - 2f, row.xMax - 70f - wx - 10f, 28f), "$" + p.Coins + "  ·  " + Mathf.RoundToInt(p.RiskChance * 100f) + "% risco" + (p.Mods.Count > 0 ? "  ·  " + p.Mods.Count + "D" : "") + (v.ShopOpen && p.Immune ? "  ·  IMUNE" : ""), 17, current ? NavyDark : Muted, TextAnchor.MiddleLeft, true);
                    if (!p.Connected) T(new Rect(row.x + 72f, row.y + sub - 2f, 170f, 28f), "DESLIGADO", 17, Coral, TextAnchor.MiddleLeft, true);
                }
                var wins = new Rect(row.xMax - 62f, row.y + 8f, 54f, rowH - 16f);
                Round(wins, current ? NavyDark : new Color(0f, 0f, 0f, 0.35f), 10f);
                T(wins, p.RoundWins.ToString(), rowH < 70f ? 28 : 34, current ? Mustard : Ivory, TextAnchor.MiddleCenter, true);
                ry += rowH + 8f;
            }
            DrawDebuffCards(net, panel.yMax + 12f, x, w);
        }

        // ------------------------------------------------------------------ fases

        void DrawPhase(NetGame net)
        {
            var v = net.View;
            var me = net.LocalPlayer;
            switch (v.Phase)
            {
                case MatchPhase.RoundIntro:
                {
                    var opener = v.RoundOrder.Count > 0 ? v.GetPlayer(v.RoundOrder[0]) : null;
                    TopCard(v.SuddenDeath ? "MORTE SÚBITA" : "RONDA " + v.RoundIndex,
                        v.FinalBall + " bolas  ·  a bola " + v.FinalBall + " decide a ronda" + (opener != null ? "  ·  abre " + PlayerName(opener) : ""), Mustard);
                    break;
                }
                case MatchPhase.AwaitShot:
                case MatchPhase.ShotWindup:
                case MatchPhase.Simulating:
                case MatchPhase.ShotResult:
                    DrawTurnStrip(net);
                    break;
                case MatchPhase.RoundWon:
                {
                    var winner = v.GetPlayer(v.RoundWinner);
                    if (winner != null)
                        TopCard(winner.Name.ToUpperInvariant() + " VENCE A RONDA!", Colored("+" + v.WinnerReward, Mustard) + " ao vencedor  ·  " + Colored("+" + v.ParticipationReward, Mustard) +
                                                                                    " aos outros  ·  o vencedor fica fora da roleta", winner.Profile.Color);
                    break;
                }
                case MatchPhase.PunishmentDraw:
                    DrawPunishmentDraw(v);
                    break;
                case MatchPhase.AwaitTrigger:
                    DrawTrigger(net);
                    break;
                case MatchPhase.RiskReveal:
                    DrawRiskReveal(net);
                    break;
                case MatchPhase.Shop:
                    DrawShopCall(net);
                    break;
                case MatchPhase.ShopReveal:
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var r in v.Purchases)
                    {
                        var buyer = v.GetPlayer(r.Buyer);
                        var target = v.GetPlayer(r.Target);
                        if (buyer != null && target != null)
                            sb.Append(PlayerName(buyer)).Append(" → ").Append(PlayerName(target)).Append(": ").Append(Colored(DefName(r.Def), DefTint(r.Def))).Append('\n');
                    }
                    TopCard("SABOTAGENS", sb.ToString().TrimEnd(), SabotageColor, 90f + v.Purchases.Count * 30f);
                    break;
                }
            }
        }

        void TopCard(string title, string body, Color accent, float height = 150f)
        {
            var r = CenterSlot(900f, 520f, height);
            float w = r.width;
            Plate(r, NavyDark);
            Round(new Rect(r.x + 14f, r.y + 10f, r.width - 28f, 6f), accent, 3f);
            T(new Rect(r.x, r.y + 18f, r.width, 64f), title, 50, accent, TextAnchor.MiddleCenter, true, true);
            T(new Rect(r.x + 30f, r.y + 84f, r.width - 60f, r.height - 94f), body, 23, Ivory, TextAnchor.UpperCenter, false, false, true);
        }

        /// <summary>
        /// Painel de turnos: quem joga (destaque), quanto tempo falta (grande; vermelho a pulsar nos últimos 5 s),
        /// quem joga a seguir, a bola legal e a bola que ganha a ronda — e, na última bola, o que é preciso para acabar.
        /// Tudo vem do snapshot do anfitrião, por isso actualiza sozinho depois de faltas e mudanças de vez.
        /// </summary>
        void DrawTurnStrip(NetGame net)
        {
            var v = net.View;
            var current = v.GetPlayer(v.CurrentPlayer);
            if (current == null) return;
            bool mine = net.LocalPlayer != null && net.LocalPlayer.Id == current.Id;
            var r = CenterSlot(680f, 560f, 118f);
            float w = r.width;
            Color ink = mine ? NavyDark : Ivory, soft = mine ? NavyDark : Muted;
            Plate(r, mine ? Mustard : NavyDark);
            HeadIcon(new Rect(r.x + 22f, r.y + 14f, 56f, 56f), current.Profile);
            // Colunas fixas: nome/barra · tempo (96) · bolas (200). Nada se sobrepõe em ecrãs estreitos.
            const float timerW = 96f, chipsW = 200f;
            float bars = w - 90f - 16f - timerW - chipsW;
            T(new Rect(r.x + 90f, r.y + 8f, bars + timerW, 48f), mine ? "A TUA VEZ" : "VEZ DE " + Colored(current.Name.ToUpperInvariant(), Color.Lerp(current.Profile.Color, Color.white, 0.35f)), 34, ink, TextAnchor.MiddleLeft, true);

            if (v.Phase == MatchPhase.AwaitShot && v.PhaseDuration > 0f)
            {
                float frac = Mathf.Clamp01(v.PhaseRemaining / v.PhaseDuration);
                bool hurry = v.PhaseRemaining <= 5f;
                var bar = new Rect(r.x + 90f, r.y + 60f, bars, 14f);
                Round(bar, new Color(0f, 0f, 0f, 0.35f), 7f);
                Round(new Rect(bar.x, bar.y, Mathf.Max(14f, bar.width * frac), bar.height), hurry || frac < 0.25f ? Coral : mine ? NavyDark : Mustard, 7f);
                float pulse = hurry && !v.MatchPaused ? 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
                T(new Rect(bar.xMax + 8f, r.y + 30f, timerW, 60f), Mathf.CeilToInt(v.PhaseRemaining).ToString(), Mathf.RoundToInt(46 * pulse), hurry ? Coral : ink, TextAnchor.MiddleCenter, true);
            }
            else if (v.Phase == MatchPhase.Simulating)
            {
                T(new Rect(r.x + 90f, r.y + 52f, bars, 30f), mine ? "bolas em movimento… anda à vontade para ver a jogada" : "bolas em movimento…", 18, soft, TextAnchor.MiddleLeft, true);
            }

            // Quem joga a seguir (próximo activo na ordem desta ronda).
            var next = v.GetPlayer(NextInOrder(v));
            if (next != null && next.Id != current.Id)
            {
                bool nextMine = net.LocalPlayer != null && net.LocalPlayer.Id == next.Id;
                HeadIcon(new Rect(r.x + 90f, r.y + 82f, 28f, 28f), next.Profile);
                T(new Rect(r.x + 124f, r.y + 80f, bars, 32f), "A SEGUIR: " + (nextMine ? "TU" : next.Name.ToUpperInvariant()), 18, nextMine ? (mine ? NavyDark : Mustard) : soft, TextAnchor.MiddleLeft, true);
            }

            int lowest = v.LowestBall;
            bool lastBall = lowest > 0 && v.ObjectBallsOnTable == 1;
            if (lowest > 0)
            {
                T(new Rect(r.xMax - chipsW, r.y + 6f, 110f, 22f), lastBall ? "ÚLTIMA" : "BOLA LEGAL", 15, lastBall ? Coral : soft, TextAnchor.MiddleCenter, true);
                if (lastBall) Round(new Rect(r.xMax - chipsW + 20f, r.y + 27f, 70f, 70f), Coral, 35f);
                BallChip(new Rect(r.xMax - chipsW + 25f, r.y + 32f, 60f, 60f), lowest);
                // A bola que ganha a ronda quando entra numa tacada legal (pode ser por combinação).
                if (!lastBall && v.FinalBall > 0 && v.FinalBall != lowest)
                {
                    T(new Rect(r.xMax - 100f, r.y + 6f, 90f, 22f), "GANHA", 15, soft, TextAnchor.MiddleCenter, true);
                    BallChip(new Rect(r.xMax - 80f, r.y + 38f, 48f, 48f), v.FinalBall);
                }
            }

            float below = r.yMax + 6f;
            if (lastBall)
            {
                var banner = new Rect(r.x + 20f, below, w - 40f, 40f);
                Round(banner, Darker(Coral, 0.85f), 12f);
                T(banner, mine ? "ÚLTIMA BOLA: toca primeiro na " + lowest + " e mete-a para ganhar a ronda" : "ÚLTIMA BOLA: quem meter a " + lowest + " (tocando-lhe primeiro) ganha a ronda", 19, Ivory, TextAnchor.MiddleCenter, true);
                below += 46f;
            }
            if (v.BallInHand && v.Phase == MatchPhase.AwaitShot) T(new Rect(r.x + 30f, below, 460f, 26f), v.KitchenOnly ? "BOLA NA MÃO (CABECEIRA)" : "BOLA NA MÃO · ONDE QUISERES", 18, Mustard, TextAnchor.MiddleLeft, true, true);
        }

        /// <summary>Próximo jogador activo na ordem da ronda depois de quem tem a vez (-1 se não houver).</summary>
        static int NextInOrder(MatchView v)
        {
            var order = v.RoundOrder;
            int at = order.IndexOf(v.CurrentPlayer);
            if (at < 0 || order.Count == 0) return -1;
            for (int step = 1; step <= order.Count; step++)
            {
                var p = v.GetPlayer(order[(at + step) % order.Count]);
                if (p != null && p.IsActive) return p.Id;
            }
            return -1;
        }

        void BallChip(Rect r, int number)
        {
            var c = VisualFactory.BallColor(number);
            Round(r, VisualFactory.IsStripe(number) ? Ivory : c, r.width * 0.5f);
            if (VisualFactory.IsStripe(number)) Round(new Rect(r.x, r.y + r.height * 0.24f, r.width, r.height * 0.52f), c, r.height * 0.2f);
            var inner = Inset(r, r.width * 0.26f);
            Round(inner, Ivory, inner.width * 0.5f);
            T(inner, number.ToString(), Mathf.RoundToInt(inner.height * 0.8f), NavyDark, TextAnchor.MiddleCenter, true);
        }

        void DrawPunishmentDraw(MatchView v)
        {
            var candidates = v.Candidates;
            int highlight = HighlightedPlayer;
            float size = 96f, gap = 22f;
            float w = Mathf.Max(560f, candidates.Count * (size + gap) + 80f);
            var r = CenterSlot(w, Mathf.Min(w, 520f), 230f);
            if (r.width < w)
            {
                size = Mathf.Max(56f, (r.width - 80f) / Mathf.Max(1, candidates.Count) - gap);
                w = r.width;
            }
            Plate(r, NavyDark);
            T(new Rect(r.x, r.y + 12f, r.width, 56f), "SORTEIO DA ROLETA", 46, Coral, TextAnchor.MiddleCenter, true, true);
            T(new Rect(r.x, r.y + 64f, r.width, 28f), candidates.Count == 1 ? "Só resta uma pessoa elegível." : "O vencedor está a salvo. Quem vai à roleta?", 20, Muted, TextAnchor.MiddleCenter);
            float x = r.x + (r.width - candidates.Count * (size + gap) + gap) * 0.5f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var p = v.GetPlayer(candidates[i]);
                if (p == null) continue;
                bool on = candidates[i] == highlight;
                float s = on ? size + 14f : size;
                var icon = new Rect(x + (size - s) * 0.5f, r.y + 104f + (size - s) * 0.5f, s, s);
                if (on) Round(new Rect(icon.x - 6f, icon.y - 6f, icon.width + 12f, icon.height + 12f), Coral, (s + 12f) * 0.5f);
                HeadIcon(icon, p.Profile);
                x += size + gap;
            }
        }

        void DrawTrigger(NetGame net)
        {
            var v = net.View;
            var p = v.GetPlayer(v.Selected);
            if (p == null) return;
            bool mine = net.LocalPlayer != null && net.LocalPlayer.Id == p.Id;
            float w = 760f;
            if (mine)
            {
                // Concept 11: placa amarela "PUXA O GATILHO" + "CLIQUE ESQUERDO".
                w = Mathf.Min(w, _vw - 80f);
                var sign = new Rect((_vw - w) * 0.5f, _vh - 360f, w, 170f);
                bool armed = TriggerArmed;
                Plate(sign, armed ? Mustard : NavyLight, true, 22f, armed);
                T(new Rect(sign.x, sign.y + 10f, sign.width, 100f), armed ? "PUXA O GATILHO" : "A PEGAR NO REVÓLVER…", armed ? 76 : 54, NavyDark, TextAnchor.MiddleCenter, true);
                T(new Rect(sign.x, sign.y + 106f, sign.width, 40f), "resolve sozinho em " + Mathf.CeilToInt(v.PhaseRemaining) + " s  ·  " + Mathf.RoundToInt((v.GetPlayer(v.Selected)?.RiskChance ?? 0f) * 100f) + "% de risco pessoal", 20, Darker(NavyDark, 1f), TextAnchor.MiddleCenter, true);
                var hold = new Rect((_vw - 520f) * 0.5f, sign.yMax + 18f, 520f, 96f);
                Plate(hold, NavyDark, true, 30f);
                var fill = Inset(hold, 12f);
                Round(fill, Darker(Mustard, 0.35f), 22f);
                Round(new Rect(fill.x, fill.y, Mathf.Max(44f, fill.width * TriggerHold), fill.height), Mustard, 22f);
                T(hold, armed ? "CLIQUE ESQUERDO" : "ESPERA…", 42, TriggerHold > 0.5f ? NavyDark : armed ? Ivory : Muted, TextAnchor.MiddleCenter, true);
            }
            else
            {
                var r = new Rect((_vw - w) * 0.5f, _vh - 200f, w, 120f);
                Plate(r, NavyDark);
                HeadIcon(new Rect(r.x + 20f, r.y + 14f, 92f, 92f), p.Profile);
                T(new Rect(r.x + 130f, r.y + 10f, r.width - 150f, 60f), p.Name.ToUpperInvariant() + " ESTÁ NA ROLETA", 36, p.Profile.Color, TextAnchor.MiddleLeft, true);
                T(new Rect(r.x + 130f, r.y + 66f, r.width - 150f, 40f), "à espera do gatilho… " + Mathf.CeilToInt(v.PhaseRemaining) + " s  ·  " + Mathf.RoundToInt((v.GetPlayer(v.Selected)?.RiskChance ?? 0f) * 100f) + "% de risco pessoal", 22, Muted, TextAnchor.MiddleLeft);
            }
        }

        void DrawRiskReveal(NetGame net)
        {
            var v = net.View;
            if (!v.HasRiskResult) return;
            var p = v.GetPlayer(v.Selected);
            if (p == null) return;
            float w = 900f;
            var r = new Rect((_vw - w) * 0.5f, 60f, w, 330f);
            if (v.RiskOutcome == RiskOutcome.Discharge)
            {
                Plate(r, NavyDark, true, 22f);
                T(new Rect(r.x, r.y + 20f, r.width, 110f), "SPLAT!", 100, ArtPalette.Goo, TextAnchor.MiddleCenter, true, true);
                string body = v.RiskEliminated ? p.Name + " morreu e está fora da partida — passa a espectador."
                    : p.Name + " leva um tiro e perde uma vida (" + p.Lives + " restante" + (p.Lives == 1 ? "" : "s") + ").";
                T(new Rect(r.x + 40f, r.y + 150f, r.width - 80f, 90f), body + (v.RiskAutomatic ? "\n(resolvido automaticamente)" : ""), 26, Ivory, TextAnchor.UpperCenter, false, false, true);
                return;
            }

            // Concept 12: cartão de resultado do debuff.
            Plate(r, Mustard, true, 22f);
            if (v.RiskModifierDef >= 0 && ValidDef(v.RiskModifierDef))
            {
                var def = NetGame.SharedCatalog[v.RiskModifierDef];
                T(new Rect(r.x, r.y + 14f, r.width, 90f), def.DisplayName.ToUpperInvariant(), 72, NavyDark, TextAnchor.MiddleCenter, true);
                var icon = new Rect(r.x + 40f, r.y + 118f, 150f, 150f);
                Round(icon, NavyDark, 75f);
                Round(Inset(icon, 10f), def.Tint, 65f);
                // Rótulos compridos ("ENSABOADO") encolhem para caber dentro do círculo.
                int labelSize = Mathf.Clamp(Mathf.FloorToInt(118f / Mathf.Max(1, def.ShortLabel.Length) / 0.5f), 20, 44);
                T(Inset(icon, 14f), def.ShortLabel, labelSize, NavyDark, TextAnchor.MiddleCenter, true);
                var desc = new Rect(r.x + 220f, r.y + 116f, r.width - 260f, 90f);
                Round(desc, Ivory, 14f);
                T(Inset(desc, 14f), def.Description, 22, NavyDark, TextAnchor.MiddleLeft, false, false, true);
                var dur = new Rect(r.x + 220f, r.y + 218f, r.width - 260f, 52f);
                Round(dur, NavyDark, 12f);
                string duration = def.DurationType == ModifierDurationType.Shots ? def.DurationValue + (def.DurationValue == 1 ? " TACADA" : " TACADAS") : def.DurationValue + (def.DurationValue == 1 ? " RONDA" : " RONDAS");
                T(dur, "DURAÇÃO: " + Colored(duration, Mustard), 24, Ivory, TextAnchor.MiddleCenter, true);
            }
            else
            {
                T(new Rect(r.x, r.y + 30f, r.width, 100f), "*CLIQUE*", 90, NavyDark, TextAnchor.MiddleCenter, true);
                T(new Rect(r.x, r.y + 150f, r.width, 60f), v.Settings.EnableDebuffs ? "Sobreviveu sem efeito (já tem o máximo de efeitos)." : "Sobreviveu! (debuffs desligados nesta sala)", 26, NavyDark, TextAnchor.MiddleCenter);
            }
            var survived = new Rect(r.center.x - 170f, r.yMax - 50f, 340f, 40f);
            Round(survived, NavyDark, 20f);
            T(survived, p.Name.ToUpperInvariant() + " SOBREVIVEU", 20, Teal, TextAnchor.MiddleCenter, true);
        }

        void DrawShopCall(NetGame net)
        {
            var v = net.View;
            var me = net.LocalPlayer;
            if (_game.Interaction.Active is SabotageMachine) return;
            bool canBuy = me != null && me.IsActive && !me.ShopPassed && me.PurchasesThisWindow < v.MaxPurchases;
            float w = 780f;
            var r = new Rect((_vw - w) * 0.5f, 24f, w, 128f);
            Plate(r, canBuy ? Violet : NavyDark, true, 20f, canBuy);
            T(new Rect(r.x + 30f, r.y + 8f, r.width - 190f, 60f), "JANELA DE SABOTAGEM", 44, Ivory, TextAnchor.MiddleLeft, true, true);
            T(new Rect(r.xMax - 160f, r.y + 8f, 130f, 60f), Mathf.CeilToInt(v.PhaseRemaining) + "s", 46, Mustard, TextAnchor.MiddleRight, true, true);
            string sub = me == null || me.Eliminated ? "Estás a ver: os vivos estão a escolher quem sabotar."
                : !canBuy ? (me.ShopPassed ? "Passaste. À espera dos outros…" : "Compra feita — a carta sai da máquina.")
                : "Vai à MÁQUINA DE SABOTAGEM e carrega [E]. Escolhes o alvo; o efeito é sorteado.";
            T(new Rect(r.x + 30f, r.y + 70f, r.width - 60f, 44f), sub, 22, Ivory, TextAnchor.MiddleLeft);

            if (canBuy) DrawWorldMarker(_game.SabotageMachine.FocusPoint + Vector3.up * 0.9f, "SABOTAGEM");
        }

        /// <summary>Marcador sobre um objecto do mundo; se estiver fora do ecrã, seta na borda.</summary>
        void DrawWorldMarker(Vector3 world, string label)
        {
            var cam = _game.Rig.Camera;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            var pos = new Vector2(sp.x / _scale, (UnityEngine.Screen.height - sp.y) / _scale);
            bool onScreen = sp.z > 0f && pos.x > 40f && pos.x < _vw - 40f && pos.y > 40f && pos.y < _vh - 40f;
            float bob = Mathf.Sin(Time.unscaledTime * 5f) * 6f;
            if (onScreen)
            {
                var b = new Rect(pos.x - 90f, pos.y - 70f + bob, 180f, 40f);
                Round(b, Violet, 20f);
                T(b, label, 20, Ivory, TextAnchor.MiddleCenter, true);
                var old = GUI.color;
                GUI.color = Violet;
                Rotate(180f, new Vector2(pos.x, pos.y - 18f + bob));
                GUI.DrawTexture(new Rect(pos.x - 14f, pos.y - 30f + bob, 28f, 24f), _triUp);
                Rotate(-180f, new Vector2(pos.x, pos.y - 18f + bob));
                GUI.color = old;
                return;
            }
            // Seta na borda (direcção projectada no plano do ecrã).
            Vector3 local = cam.transform.InverseTransformPoint(world);
            var dir = new Vector2(local.x, -local.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
            dir.Normalize();
            var center = new Vector2(_vw * 0.5f, _vh * 0.5f);
            float k = Mathf.Min((_vw * 0.5f - 70f) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (_vh * 0.5f - 70f) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
            var edge = center + dir * k;
            Round(new Rect(edge.x - 30f, edge.y - 30f, 60f, 60f), Violet, 30f);
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var arrowRect = new Rect(edge.x - 14f, edge.y - 14f, 28f, 28f);
            var oldc = GUI.color;
            GUI.color = Ivory;
            Rotate(angle, edge);
            GUI.DrawTexture(arrowRect, _triRight);
            Rotate(-angle, edge);
            GUI.color = oldc;
        }

        // ------------------------------------------------------------------ tacada (concept 08, barra inferior)

        void DrawShotPrompts(NetGame net)
        {
            var v = net.View;
            var shot = _game.Shot;
            float cx = _vw * 0.5f;
            if (shot.CanTakeShot && _game.Interaction.Focused == null)
                PromptPill(cx, _vh - 150f, "<b>[E]</b> PEGAR NO TACO" + (v.BallInHand ? (v.KitchenOnly ? "  ·  BOLA NA MÃO (ATRÁS DA LINHA)" : "  ·  BOLA NA MÃO: PÕE A BRANCA ONDE QUISERES") : ""));
            else if (shot.State == ShotState.Placing)
            {
                PromptPill(cx, _vh - 150f, "BOLA NA MÃO" + (v.KitchenOnly ? " (ATRÁS DA LINHA)" : "") + ": aponta para a mesa e <b>clica</b> para pousar  ·  <b>[E]</b> manter  ·  <b>[Q]</b> sair");
                string hint = shot.PlacementHint;
                if (!string.IsNullOrEmpty(hint)) SubPill(cx, _vh - 196f, hint, Coral);
                else if (shot.PlacementVisible && shot.PlacementValid) SubPill(cx, _vh - 196f, "Sítio livre", GoodGreen);
            }
            else if (shot.State == ShotState.PlacePending)
                PromptPill(cx, _vh - 150f, "A CONFIRMAR COM O ANFITRIÃO…");
            else if (shot.State == ShotState.Aiming)
                DrawShotPanel(shot, net);
            if (Time.unscaledTime - shot.LastRefusalTime < 2.2f && !string.IsNullOrEmpty(shot.LastRefusal))
                SubPill(cx, _vh - 244f, "RECUSADO: " + shot.LastRefusal, Coral);

            var me = net.LocalPlayer;
            if (me != null && me.Mods.Count > 0 && v.CurrentPlayer == me.Id && v.Phase == MatchPhase.AwaitShot && shot.State == ShotState.Aiming)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var m in me.Mods)
                {
                    if (!ValidDef(m.Def)) continue;
                    if (sb.Length > 0) sb.Append("   ·   ");
                    sb.Append(Colored(EffectNote(NetGame.SharedCatalog[m.Def]).ToUpperInvariant(), DefTint(m.Def)));
                }
                float ew = Mathf.Min(1100f, _vw - 60f);
                var r = new Rect(cx - ew * 0.5f, _vh - 206f, ew, 40f);
                Round(r, new Color(0.35f, 0.08f, 0.2f, 0.9f), 20f);
                T(r, sb.ToString(), 18, Ivory, TextAnchor.MiddleCenter, true);
            }
        }

        void SubPill(float cx, float y, string text, Color color)
        {
            float w = Mathf.Min(700f, _vw - 60f);
            var r = new Rect(cx - w * 0.5f, y, w, 38f);
            Round(r, new Color(0.03f, 0.05f, 0.09f, 0.8f), 19f);
            T(r, text, 18, color, TextAnchor.MiddleCenter, true);
        }

        /// <summary>O que cada efeito activo faz à tacada (a linha branca não o mostra — é o jogador que compensa).</summary>
        static string EffectNote(ModifierDefinition def)
        {
            foreach (var e in def.Effects)
                switch (e.Kind)
                {
                    case ModifierEffectKind.CurveAcceleration: return def.DisplayName + (e.Value > 0f ? ": a bola curva para a direita" : ": a bola curva para a esquerda");
                    case ModifierEffectKind.ImpulseScale: return def.DisplayName + ": força ×" + e.Value.ToString("0.00");
                    case ModifierEffectKind.ShotAngleNoise: return def.DisplayName + ": a tacada sai com um desvio aleatório";
                    case ModifierEffectKind.AimSway: return def.DisplayName + ": a mira oscila";
                }
            return def.DisplayName;
        }

        void PromptPill(float cx, float y, string text)
        {
            float w = Mathf.Min(900f, _vw - 60f);
            var r = new Rect(cx - w * 0.5f, y, w, 56f);
            Round(r, new Color(0.04f, 0.06f, 0.1f, 0.85f), 28f);
            Outline(r, Mustard, 2f, 28f);
            T(r, text, 24, Mustard, TextAnchor.MiddleCenter, true);
        }

        void DrawShotPanel(ShotController shot, NetGame net)
        {
            float w = Mathf.Min(1500f, _vw - 60f), h = 140f;
            var r = new Rect((_vw - w) * 0.5f, _vh - h - 20f, w, h);
            Plate(r, NavyDark);
            // Estreito (interface grande / 4:3): sem o mostrador "MIRA" nem a placa de dica; a força nunca encolhe demais.
            bool compact = w < 1300f;

            float x = r.x + 30f;
            if (!compact)
            {
                T(new Rect(x, r.y + 12f, 150f, 30f), "MIRA", 22, Ivory, TextAnchor.MiddleCenter, true);
                var dial = new Rect(x + 35f, r.y + 46f, 80f, 80f);
                Outline(dial, Ivory, 3f, 40f);
                T(dial, "RATO", 18, Ivory, TextAnchor.MiddleCenter, true);
                x += 180f;
                Fill(new Rect(x, r.y + 20f, 2f, h - 40f), NavyLight);
                x += 30f;
            }

            // Força segmentada (com a força real, se um debuff a mudar).
            float barW = compact ? w - 390f : w - 960f;
            var effective = shot.PreviewShot(net);
            string force = shot.Pulling ? "FORÇA — A RECUAR…" : "FORÇA";
            if (!Mathf.Approximately(effective.ImpulseScale, 1f)) force += Colored("   ×" + effective.ImpulseScale.ToString("0.00") + " (DEBUFF)", Coral);
            T(new Rect(x, r.y + 12f, barW, 30f), force, 22, Ivory, TextAnchor.MiddleLeft, true);
            var bar = new Rect(x, r.y + 52f, barW, 44f);
            Round(bar, new Color(0f, 0f, 0f, 0.4f), 22f);
            int segments = 20;
            float power = shot.Pull01;
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                var seg = new Rect(bar.x + 8f + i * (bar.width - 16f) / segments, bar.y + 8f, (bar.width - 16f) / segments - 3f, bar.height - 16f);
                Color on = Color.Lerp(Mustard, Coral, Mathf.InverseLerp(0.55f, 1f, t0));
                Round(seg, t0 < power ? on : new Color(1f, 1f, 1f, 0.08f), 3f);
            }
            float markX = bar.x + 8f + (bar.width - 16f) * power;
            Round(new Rect(markX - 3f, bar.y - 6f, 6f, bar.height + 12f), Ivory, 3f);
            T(new Rect(x, r.y + 102f, barW, 26f), compact ? "clique e puxa para trás · solta · Shift fino · Tab cima · Q largar" : "segura clique e puxa o rato para trás · solta para tacar · Shift = fino · Tab = vista de cima · Q = largar", 16, Muted, TextAnchor.MiddleLeft);
            x += barW + 30f;
            Fill(new Rect(x, r.y + 20f, 2f, h - 40f), NavyLight);
            x += 30f;

            // Efeito.
            T(new Rect(x, r.y + 12f, 130f, 30f), "EFEITO", 22, Ivory, TextAnchor.MiddleCenter, true);
            var ball = new Rect(x + 25f, r.y + 46f, 80f, 80f);
            Round(ball, Ivory, 40f);
            Fill(new Rect(ball.center.x - 0.5f, ball.y + 6f, 1f, ball.height - 12f), new Color(0f, 0f, 0f, 0.2f));
            Fill(new Rect(ball.x + 6f, ball.center.y - 0.5f, ball.width - 12f, 1f), new Color(0f, 0f, 0f, 0.2f));
            var dot = new Rect(ball.center.x + shot.Spin.x * 28f - 8f, ball.center.y - shot.Spin.y * 28f - 8f, 16f, 16f);
            Round(dot, ArtPalette.Red, 8f);
            string spin = shot.Spin.y > 0.25f ? "ALTO" : shot.Spin.y < -0.25f ? "BAIXO" : "CENTRO";
            T(new Rect(x + 130f, r.y + 50f, 150f, 30f), spin, 22, Mustard, TextAnchor.MiddleLeft, true);
            T(new Rect(x + 130f, r.y + 84f, 170f, 40f), "W A S D · C limpa", 17, Muted, TextAnchor.MiddleLeft);
            x += 300f;
            if (compact) return;
            Fill(new Rect(x, r.y + 20f, 2f, h - 40f), NavyLight);

            var tip = new Rect(x + 24f, r.y + 30f, r.xMax - x - 48f, 80f);
            Plate(tip, Mustard, true, 18f);
            T(tip, shot.Pulling ? "SOLTA PARA TACAR" : "CLIQUE E PUXA", 26, NavyDark, TextAnchor.MiddleCenter, true);
        }

        // ------------------------------------------------------------------ máquina de sabotagem (concept 14)

        void DrawMachineScreen(NetGame net, SabotageMachine machine)
        {
            if (_game.Interaction.ZoomBlend < 0.82f) return;
            var cam = _game.Rig.Camera;
            var corners = machine.ScreenCorners;
            if (cam == null || corners == null) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var sp = cam.WorldToScreenPoint(c);
                float gx = sp.x / _scale, gy = (UnityEngine.Screen.height - sp.y) / _scale;
                minX = Mathf.Min(minX, gx);
                maxX = Mathf.Max(maxX, gx);
                minY = Mathf.Min(minY, gy);
                maxY = Mathf.Max(maxY, gy);
            }
            var screen = Rect.MinMaxRect(Mathf.Max(10f, minX), Mathf.Max(10f, minY), Mathf.Min(_vw - 10f, maxX), Mathf.Min(_vh - 10f, maxY));
            if (screen.width < 400f || screen.height < 300f) return;

            var v = net.View;
            var me = net.LocalPlayer;
            float fade = Mathf.InverseLerp(0.82f, 1f, _game.Interaction.ZoomBlend);
            Round(screen, new Color(0.03f, 0.05f, 0.1f, 0.96f * fade), 18f);
            // Linhas de varrimento de CRT.
            for (float yy = screen.y; yy < screen.yMax; yy += 4f) Fill(new Rect(screen.x, yy, screen.width, 1f), new Color(1f, 1f, 1f, 0.025f));
            var area = Inset(screen, 22f);

            // Cabeçalho mostarda + moedas.
            var head = new Rect(area.x, area.y, area.width - 250f, 92f);
            Plate(head, Mustard, true, 16f);
            T(head, "MÁQUINA DE SABOTAGEM", 50, NavyDark, TextAnchor.MiddleCenter, true);
            var wallet = new Rect(head.xMax + 14f, area.y, 236f, 92f);
            Plate(wallet, NavyDark, true, 16f);
            Coin(new Rect(wallet.x + 24f, wallet.y + 20f, 52f, 52f));
            T(new Rect(wallet.x + 88f, wallet.y, 140f, wallet.height), me != null ? me.Coins.ToString() : "—", 46, Ivory, TextAnchor.MiddleLeft, true);

            float y = head.yMax + 16f;
            T(new Rect(area.x, y, area.width * 0.6f, 32f), "Escolhe o alvo. O efeito sai à sorte do nível que pagares.", 21, Ivory);
            string clock = v.SabotageAnytime
                ? (me != null && me.SabotageCooldown > 0f ? "RECARGA " + Mathf.CeilToInt(me.SabotageCooldown) + " s" : "PRONTA")
                : Mathf.CeilToInt(v.PhaseRemaining) + " s";
            T(new Rect(area.x, y, area.width, 32f), clock, 28, me != null && me.SabotageCooldown > 0f ? Coral : Mustard, TextAnchor.MiddleRight, true);
            y += 42f;

            // Preços reais (os mesmos que o anfitrião cobra), o saldo e se dá para pagar cada nível.
            {
                int prank = v.PrankCost > 0 ? v.PrankCost : v.Settings.SabotageCost;
                int chaos = v.ChaosCost > 0 ? v.ChaosCost : v.Settings.SabotageCost * 2;
                int coins = me != null ? me.Coins : 0;
                var strip = new Rect(area.x, y, area.width, 50f);
                Round(strip, new Color(0f, 0f, 0f, 0.35f), 12f);
                float cw = strip.width / 3f;
                void Price(float px, string label, int cost, Color accent)
                {
                    T(new Rect(px + 16f, strip.y, cw - 32f, strip.height), Colored(label, accent) + "  BASE $" + cost,
                        20, Ivory, TextAnchor.MiddleLeft, true);
                }
                Price(strip.x, "TRAVESSURA", prank, Mustard);
                Price(strip.x + cw, "CAOS", chaos, Coral);
                T(new Rect(strip.x + cw * 2f + 16f, strip.y, cw - 32f, strip.height), "O TEU SALDO  " + Colored("$" + coins, Mustard), 20, Ivory, TextAnchor.MiddleRight, true);
                y += 60f;
            }

            if (me == null || !me.IsActive)
            {
                T(new Rect(area.x, y + 40f, area.width, 60f), "Só os jogadores vivos podem sabotar.", 30, Muted, TextAnchor.MiddleCenter, true);
                return;
            }
            bool bought = v.SabotageAnytime ? me.SabotageCooldown > 0f : me.PurchasesThisWindow >= v.MaxPurchases;

            var targets = new List<PlayerView>();
            foreach (var p in v.Players)
                if (p.Id != me.Id) targets.Add(p);
            float cardGap = 14f;
            float footer = 96f;
            float cardH = area.yMax - y - footer;
            float cardW = Mathf.Min(300f, (area.width - cardGap * (targets.Count - 1)) / Mathf.Max(1, targets.Count));
            float cx = area.x + (area.width - (cardW * targets.Count + cardGap * (targets.Count - 1))) * 0.5f;
            foreach (var target in targets)
            {
                DrawTargetCard(net, new Rect(cx, y, cardW, cardH), me, target, bought);
                cx += cardW + cardGap;
            }

            var foot = new Rect(area.x, area.yMax - footer + 14f, area.width, footer - 14f);
            if (Secondary(new Rect(foot.x, foot.y, 260f, foot.height), bought || v.SabotageAnytime ? "SAIR" : "PASSAR", true, 28))
            {
                if (!bought && !v.SabotageAnytime) net.RequestPassShop();
                _game.Interaction.Close();
            }
            string legend = Colored("TRAVESSURA", Mustard) + ": " + TierNames(SabotageTier.Prank) + "\n" + Colored("CAOS", Coral) + ": " + TierNames(SabotageTier.Chaos);
            T(new Rect(foot.x + 290f, foot.y, foot.width - 290f, foot.height), legend, 18, Ivory, TextAnchor.MiddleLeft, false, false, true);
        }

        string TierNames(SabotageTier tier)
        {
            _tierScratch.Clear();
            var catalog = NetGame.SharedCatalog;
            if (catalog != null)
                foreach (var d in catalog)
                    if (d != null && d.Tier == tier) _tierScratch.Add(d);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _tierScratch.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_tierScratch[i].DisplayName);
            }
            return sb.ToString();
        }

        void DrawTargetCard(NetGame net, Rect r, PlayerView me, PlayerView target, bool bought)
        {
            var v = net.View;
            Plate(r, NavyDark, true, 16f);
            var head = new Rect(r.x + 8f, r.y + 8f, r.width - 16f, 56f);
            Round(head, target.Profile.Color, 12f);
            T(new Rect(head.x + 14f, head.y, head.width - 120f, head.height), target.Name.ToUpperInvariant(), 26, NavyDark, TextAnchor.MiddleLeft, true);
            string state = target.Eliminated ? "FORA" : v.ShopOpen && target.Immune ? (target.ImmuneFor > 0f ? "IMUNE " + Mathf.CeilToInt(target.ImmuneFor) + "s" : "IMUNE") : "ACTIVO";
            Pill(new Rect(head.xMax - 114f, head.y + 12f, 106f, 32f), state, NavyDark, state == "ACTIVO" ? GoodGreen : state.StartsWith("IMUNE") ? Teal : Coral, 16);

            float portrait = Mathf.Min(r.width - 80f, r.height - 330f);
            portrait = Mathf.Max(portrait, 70f);
            HeadIcon(new Rect(r.center.x - portrait * 0.5f, head.yMax + 12f, portrait, portrait), target.Profile, target.Eliminated);
            float y = head.yMax + 20f + portrait;

            var debuffs = new Rect(r.x + 12f, y, r.width - 24f, 50f);
            Round(debuffs, new Color(0f, 0f, 0f, 0.35f), 10f);
            string current = target.Mods.Count == 0 ? "SEM DEBUFFS" : "";
            for (int i = 0; i < target.Mods.Count; i++) current += (i > 0 ? " · " : "") + DefLabel(target.Mods[i].Def);
            T(debuffs, current, 20, target.Mods.Count == 0 ? Muted : SabotageColor, TextAnchor.MiddleCenter, true);
            y += 60f;
            T(new Rect(r.x + 12f, y, r.width - 24f, 26f), "★ " + target.RoundWins + " rondas  ·  $" + target.Coins, 18, Muted, TextAnchor.MiddleCenter, true);
            y += 32f;

            for (int tier = 0; tier < MatchView.Tiers; tier++)
            {
                var block = v.BlockFor(me.Id, target.Id, tier);
                int cost = v.CostFor(me.Id, target.Id, tier);
                var b = new Rect(r.x + 12f, y, r.width - 24f, 58f);
                string name = tier == 0 ? "TRAVESSURA" : "CAOS";
                if (block == SabotageBlock.None && !bought)
                {
                    int baseCost = tier == 0 ? (v.PrankCost > 0 ? v.PrankCost : v.Settings.SabotageCost) : (v.ChaosCost > 0 ? v.ChaosCost : v.Settings.SabotageCost * 2);
                    string price = "$" + cost + (cost < baseCost ? " ↓ desconto" : cost > baseCost ? " ↑ repetido" : "");
                    if (Btn(b, name + "  " + price, tier == 0 ? Mustard : Coral, NavyDark, true, 22))
                    {
                        net.RequestPurchase(target.Id, tier);
                    }
                }
                else
                {
                    string why = bought ? (v.SabotageAnytime ? "RECARGA " + Mathf.CeilToInt(me.SabotageCooldown) + "s" : name) : SabotageService.Describe(block).ToUpperInvariant();
                    Btn(b, name + " $" + cost + "\n" + why, NavyLight, Ivory, false, 16);
                }
                y += 66f;
            }
        }

        // ------------------------------------------------------------------ jukebox

        // ------------------------------------------------------------------ eliminação e resultados

        void DrawEliminated(NetGame net)
        {
            var me = net.LocalPlayer;
            var r = new Rect(60f, 120f, Mathf.Min(900f, _vw - 120f), 640f);
            Round(r, new Color(0.05f, 0.08f, 0.14f, 0.9f), 26f);
            Outline(r, new Color(0.4f, 0.55f, 0.8f, 0.6f), 2f, 26f);
            T(new Rect(r.x + 60f, r.y + 30f, r.width - 120f, 170f), "ESTÁS FORA!", 150, Coral, TextAnchor.MiddleLeft, true, true);
            if (me != null) HeadIcon(new Rect(r.x + 70f, r.y + 220f, 200f, 200f), me.Profile);
            T(new Rect(r.x + 300f, r.y + 220f, r.width - 360f, 200f),
                "O tambor tinha bala: morreste e estás fora desta partida. Como espectador podes seguir qualquer jogador (clique), ver a mesa de cima ou voar como fantasma ([V]), atirar coisas (G), mandar emotes e mexer na jukebox.",
                26, Ivory, TextAnchor.MiddleLeft, false, false, true);
            if (Primary(new Rect(r.x + 60f, r.yMax - 150f, 420f, 96f), "ESPECTAR", true, 42, IconKind.Play)) _eliminatedOverlay = false;
            if (Secondary(new Rect(r.x + 500f, r.yMax - 140f, 300f, 80f), "SAIR", true, 30, IconKind.Door)) _game.Leave();
        }

        /// <summary>Barra do modo espectador: quem estou a ver e os controlos.</summary>
        void DrawSpectatorBar(NetGame net)
        {
            var spec = _game.Spectator;
            if (spec == null || !spec.Active) return;
            var v = net.View;
            float w = 860f;
            var r = new Rect((_vw - w) * 0.5f, _vh - 118f, w, 94f);
            Plate(r, NavyDark, true, 22f);
            Pill(new Rect(r.x + 18f, r.y + 14f, 170f, 30f), "ESPECTADOR", Violet, Ivory, 17);
            var target = v.GetPlayer(spec.TargetId);
            if (spec.Current != SpectatorCamera.Mode.Free && target != null)
            {
                HeadIcon(new Rect(r.x + 200f, r.y + 10f, 74f, 74f), target.Profile);
                T(new Rect(r.x + 286f, r.y + 8f, 360f, 44f), "A VER " + Colored(target.Name.ToUpperInvariant(), Color.Lerp(target.Profile.Color, Color.white, 0.3f)), 30, Ivory, TextAnchor.MiddleLeft, true);
                T(new Rect(r.x + 286f, r.y + 50f, 360f, 30f), spec.Automatic ? "câmara automática (segue a acção)" : "escolhido por ti  ·  [R] automático", 17, Muted, TextAnchor.MiddleLeft);
            }
            else
            {
                T(new Rect(r.x + 200f, r.y + 8f, 440f, 44f), spec.ModeName, 30, Ivory, TextAnchor.MiddleLeft, true);
                T(new Rect(r.x + 200f, r.y + 50f, 440f, 30f), spec.Current == SpectatorCamera.Mode.Free ? "WASD voar  ·  Espaço/Ctrl subir/descer  ·  G atirar coisas" : "a mesa inteira, a rodar devagar", 17, Muted, TextAnchor.MiddleLeft);
            }
            T(new Rect(r.xMax - 250f, r.y + 10f, 230f, 26f), "[CLIQUE] TROCAR JOGADOR", 16, Mustard, TextAnchor.MiddleRight, true);
            T(new Rect(r.xMax - 250f, r.y + 36f, 230f, 26f), "[V] " + spec.ModeName, 16, Mustard, TextAnchor.MiddleRight, true);
            T(new Rect(r.xMax - 250f, r.y + 62f, 230f, 26f), "[RODA] ZOOM", 16, Muted, TextAnchor.MiddleRight, true);
        }

        void DrawResults(NetGame net)
        {
            var v = net.View;
            var winner = v.GetPlayer(v.MatchWinner);
            if (winner == null) return;
            Fill(new Rect(0f, 0f, _vw, _vh), new Color(0.02f, 0.03f, 0.06f, 0.6f));
            bool iWon = net.LocalPlayer != null && net.LocalPlayer.Id == winner.Id;

            float w = Mathf.Min(1500f, _vw - 60f), x = (_vw - w) * 0.5f;
            var banner = new Rect(x, 30f, w, 190f);
            Plate(banner, Mustard, true, 24f, true);
            HeadIcon(new Rect(banner.x + 30f, banner.y + 20f, 150f, 150f), winner.Profile);
            T(new Rect(banner.x + 210f, banner.y + 14f, w - 240f, 110f), iWon ? "VITÓRIA!" : winner.Name.ToUpperInvariant() + " VENCE!", 96, NavyDark, TextAnchor.MiddleLeft, true);
            T(new Rect(banner.x + 214f, banner.y + 118f, w - 240f, 50f), winner.RoundWins + " rondas ganhas  ·  " + winner.Profile.Wins + (winner.Profile.Wins == 1 ? " vitória" : " vitórias") + " no histórico  ·  $" + winner.CoinsEarned + " ganhos", 28, NavyDark, TextAnchor.MiddleLeft, true);

            var table = new Rect(x, banner.yMax + 20f, w, 90f + v.Ranking.Count * 64f);
            Plate(table, NavyDark, true, 20f);
            string[] headers = { "#", "JOGADOR", "RONDAS", "VITÓRIAS", "BOLAS", "FALTAS", "SABOTOU", "SABOTADO", "GANHOU", "GASTOU" };
            float[] cols = { 30f, 90f, 520f, 640f, 750f, 860f, 970f, 1090f, 1220f, 1340f };
            float scaleX = (w - 60f) / 1440f;
            float ty = table.y + 24f;
            for (int i = 0; i < headers.Length; i++) T(new Rect(table.x + cols[i] * scaleX, ty, 160f, 34f), headers[i], 20, Muted, TextAnchor.MiddleLeft, true);
            ty += 46f;
            for (int rank = 0; rank < v.Ranking.Count; rank++)
            {
                var p = v.GetPlayer(v.Ranking[rank]);
                if (p == null) continue;
                var row = new Rect(table.x + 16f, ty, table.width - 32f, 56f);
                Round(row, rank == 0 ? Color.Lerp(NavyDark, Mustard, 0.3f) : Navy, 12f);
                HeadIcon(new Rect(table.x + cols[1] * scaleX, ty + 6f, 44f, 44f), p.Profile, p.Eliminated, false);
                string[] values =
                {
                    (rank + 1).ToString(), p.Name + (p.Eliminated ? Colored("  (fora R" + p.EliminatedRound + ")", Muted) : ""),
                    p.RoundWins.ToString(), p.Profile.Wins.ToString(), p.LegalPockets.ToString(), p.Fouls.ToString(),
                    p.SabotagesDone.ToString(), p.SabotagesReceived.ToString(), "$" + p.CoinsEarned, "$" + p.CoinsSpent
                };
                for (int i = 0; i < values.Length; i++)
                {
                    float cxp = table.x + cols[i] * scaleX + (i == 1 ? 56f : 0f);
                    T(new Rect(cxp, ty, i == 1 ? 360f * scaleX : 140f, 56f), values[i], i <= 1 ? 26 : 24, i == 1 ? p.Profile.Color : Ivory, TextAnchor.MiddleLeft, true);
                }
                ty += 64f;
            }

            var t = v.Telemetry;
            var mods = new List<string>();
            foreach (var kv in t.ModifierCounts) mods.Add(DefName(kv.Key) + " ×" + kv.Value);
            string stats = "Duração " + Mathf.FloorToInt(t.Duration / 60f) + ":" + Mathf.FloorToInt(t.Duration % 60f).ToString("00") + "  ·  tacadas " + t.Shots +
                           "  ·  faltas " + t.Fouls + "  ·  roletas " + t.RiskEvents + "  ·  splats " + t.Discharges + "  ·  sabotagens " + t.Sabotages +
                           "\nEfeitos: " + (mods.Count == 0 ? "nenhum" : string.Join(", ", mods));
            T(new Rect(x + 10f, table.yMax + 12f, w - 20f, 70f), stats, 20, Ivory, TextAnchor.UpperLeft, false, true, true);

            float by = _vh - 130f, gap = 16f;
            float bw = Mathf.Min(320f, (_vw - 60f - gap * 3f) / 4f);
            float bx = (_vw - (bw * 4f + gap * 3f)) * 0.5f;
            int size = bw < 280f ? 24 : 30;
            if (net.IsLocalHost)
            {
                if (Primary(new Rect(bx, by, bw, 96f), "REVANCHE", true, size + 6, IconKind.Play)) net.HostStartMatch();
                if (Secondary(new Rect(bx + (bw + gap), by, bw, 96f), "VOLTAR À SALA", true, size - 4, IconKind.People)) net.HostReturnToLobby();
            }
            else
            {
                T(new Rect(bx, by, bw * 2f + gap, 96f), "O anfitrião decide se há revanche.", 24, Muted, TextAnchor.MiddleCenter, true);
            }
            if (SuccessBtn(new Rect(bx + (bw + gap) * 2f, by, bw, 96f), "MODO LIVRE", true, size, IconKind.People)) HandleTab();
            if (DangerBtn(new Rect(bx + (bw + gap) * 3f, by, bw, 96f), "SAIR", true, size + 4, IconKind.Door)) _game.Leave();
            T(new Rect(0f, by - 34f, _vw, 28f), "MODO LIVRE: fecha o placar e brinca na sala (" + Colored(winner.Name, winner.Profile.Color) + " tem o revólver) · [TAB] volta a abrir", 18, Ivory, TextAnchor.MiddleCenter, true, true);
            if (net.IsLocalHost && !string.IsNullOrEmpty(net.LastLogPath)) T(new Rect(20f, _vh - 28f, _vw - 40f, 24f), "Log: " + net.LastLogPath, 14, Muted);
        }

        // ------------------------------------------------------------------ feed, flutuantes, faixas, chat

        void DrawFeed()
        {
            float w = 560f, x = 24f, y = _vh - 210f;
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                var line = _feed[i];
                float age = Time.unscaledTime - line.Time;
                float alpha = Mathf.Clamp01(1f - (age - 8f) / 2f);
                if (alpha <= 0f) continue;
                y -= 34f;
                Round(new Rect(x, y, w, 30f), new Color(0.03f, 0.05f, 0.09f, 0.6f * alpha), 8f);
                var c = line.Color;
                c.a = alpha;
                T(new Rect(x + 12f, y, w - 24f, 30f), line.Text, 18, c);
            }
        }

        void DrawFloaters()
        {
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                float age = Time.unscaledTime - f.Time;
                if (age > 2.6f)
                {
                    _floaters.RemoveAt(i);
                    continue;
                }
                if (!_rowRects.TryGetValue(f.PlayerId, out var row)) continue;
                var c = f.Color;
                c.a = Mathf.Clamp01(2.6f - age);
                T(new Rect(row.x - 470f, row.y + 16f - age * 18f, 450f, 36f), f.Text, 24, c, TextAnchor.MiddleRight, true, true);
            }
        }

        void DrawBanner()
        {
            if (_banner == null || Time.unscaledTime > _bannerUntil) return;
            float remaining = _bannerUntil - Time.unscaledTime;
            float pop = Mathf.Clamp01((Time.unscaledTime - (_bannerUntil - 3f)) * 6f);
            float w = Mathf.Min(1100f, _vw - 100f);
            var r = new Rect((_vw - w) * 0.5f, _vh * 0.42f, w, 80f);
            var c = _bannerColor;
            c.a = Mathf.Clamp01(remaining * 3f);
            Round(r, new Color(0.03f, 0.05f, 0.09f, 0.72f * c.a), 40f);
            T(r, _banner, Mathf.RoundToInt(44f + (1f - pop) * 6f), c, TextAnchor.MiddleCenter, true, true);
        }

        void DrawToast()
        {
            if (_toast == null || Time.unscaledTime > _toastUntil) return;
            float w = 820f;
            var r = new Rect((_vw - w) * 0.5f, _vh - 300f, w, 50f);
            Round(r, new Color(0.03f, 0.05f, 0.09f, 0.9f), 25f);
            Outline(r, Mustard, 2f, 25f);
            T(r, _toast, 20, Ivory, TextAnchor.MiddleCenter);
        }

        void DrawHelp(NetGame net)
        {
            string text = net.View.InMatch
                ? "WASD andar · rato olhar · E interagir · botão do meio marcar · 1-4 emotes · T chat · G atirar · Tab rato · " + (net.CanPauseMatch ? "P pausa · " : "") + "Esc menu · F1 esconder"
                : "WASD andar · E interagir · R pronto · botão do meio marcar · 1-4 emotes · T chat · G atirar · Tab rato · Esc menu · F1 esconder";
            T(new Rect(24f, _vh - 34f, _vw - 48f, 28f), text, 16, Muted);
        }

        void DrawChat(NetGame net)
        {
            if (!_chatOpen) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                net.SendChat(_chatText);
                CloseChat();
                e.Use();
                return;
            }
            float w = 640f, y = _vh - 150f;
            Round(new Rect(18f, y - 40f, w + 12f, 96f), new Color(0.03f, 0.05f, 0.09f, 0.85f), 12f);
            T(new Rect(30f, y - 36f, w, 28f), "CHAT — Enter envia · Esc cancela", 16, Muted, TextAnchor.MiddleLeft, true);
            TextField(new Rect(26f, y - 4f, w - 4f, 48f), ref _chatText, 90, "chat");
            if (_focusChat)
            {
                GUI.FocusControl("chat");
                _focusChat = false;
            }
        }
    }
}
