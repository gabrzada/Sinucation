using System.Collections.Generic;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Peças da HUD acrescentadas nesta ronda: pontos animados, cartas de debuff, frame de impacto da roleta, aviso de
    /// áudio da jukebox partilhada, marcadores, modo de brincadeira e o posicionamento responsivo das placas centrais.
    /// Tudo é desenhado dentro da matriz da HUD, por isso respeita o "Tamanho da interface".
    /// </summary>
    public sealed partial class HudView
    {
        // ================================================================== pontos (+10, +100…)

        struct ScorePop
        {
            public int PlayerId;
            public int Amount;
            public string Reason;
            public float Time;
            public int Lane;
        }

        readonly List<ScorePop> _scores = new List<ScorePop>();
        float _walletShown = -1f;
        float _walletBump;

        /// <summary>Cada crédito/débito real (EconomyService.BalanceChanged) aparece uma vez, com o valor efectivamente dado.</summary>
        void AddScore(int playerId, int amount, string reason)
        {
            // Pontos próximos do mesmo jogador empilham-se em "faixas" (não se sobrepõem).
            int lane = 0;
            float now = Time.unscaledTime;
            foreach (var s in _scores)
                if (s.PlayerId == playerId && now - s.Time < 0.9f) lane = Mathf.Max(lane, s.Lane + 1);
            _scores.Add(new ScorePop { PlayerId = playerId, Amount = amount, Reason = reason ?? "", Time = now, Lane = Mathf.Min(lane, 3) });
            var me = _bound != null ? _bound.LocalPlayer : null;
            if (me != null && me.Id == playerId) _walletBump = 1f;
        }

        const float ScoreLife = 1.7f;

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        /// <summary>
        /// Os meus pontos saem da moeda da carteira e sobem; os dos outros saem da linha deles no placar.
        /// Entrada com escala elástica, subida com abrandamento e desaparecimento suave no fim.
        /// </summary>
        void DrawScores(Rect walletRect)
        {
            float now = Time.unscaledTime;
            var me = _bound != null ? _bound.LocalPlayer : null;
            for (int i = _scores.Count - 1; i >= 0; i--)
            {
                var s = _scores[i];
                float age = now - s.Time;
                if (age > ScoreLife)
                {
                    _scores.RemoveAt(i);
                    continue;
                }
                float t = age / ScoreLife;
                float pop = age < 0.28f ? EaseOutBack(age / 0.28f) : 1f;
                float rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / 1.2f), 3f);
                float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, t));
                bool mine = me != null && s.PlayerId == me.Id;
                Vector2 anchor;
                float size;
                if (mine)
                {
                    anchor = new Vector2(walletRect.center.x, walletRect.yMax + 34f + s.Lane * 46f);
                    size = 46f;
                }
                else
                {
                    if (!_rowRects.TryGetValue(s.PlayerId, out var row)) continue;
                    anchor = new Vector2(row.x - 120f, row.center.y + s.Lane * 30f);
                    size = 30f;
                }
                float y = anchor.y - rise * (mine ? 38f : 26f);
                var color = s.Amount >= 0 ? Mustard : Coral;
                color.a = alpha;
                int fontSize = Mathf.RoundToInt(size * Mathf.Max(0.2f, pop));
                string amount = (s.Amount >= 0 ? "+" : "−") + Mathf.Abs(s.Amount);
                var box = new Rect(anchor.x - 170f, y - size * 0.6f, 340f, size * 1.2f);
                T(box, amount, fontSize, color, TextAnchor.MiddleCenter, true, true);
                if (mine && !string.IsNullOrEmpty(s.Reason))
                    T(new Rect(box.x, box.yMax - 8f, box.width, 24f), s.Reason.ToUpperInvariant(), 15, new Color(Ivory.r, Ivory.g, Ivory.b, alpha * 0.85f), TextAnchor.MiddleCenter, true, true);
            }
        }

        /// <summary>Saldo mostrado a contar até ao valor real (o valor real vem sempre do snapshot).</summary>
        int WalletDisplay(int coins)
        {
            if (_walletShown < 0f || Mathf.Abs(_walletShown - coins) > 500f) _walletShown = coins;
            _walletShown = Mathf.MoveTowards(_walletShown, coins, Mathf.Max(40f, Mathf.Abs(coins - _walletShown) * 4f) * Time.unscaledDeltaTime);
            _walletBump = Mathf.MoveTowards(_walletBump, 0f, Time.unscaledDeltaTime * 3f);
            return Mathf.RoundToInt(_walletShown);
        }

        // ================================================================== cartas de debuff

        readonly Dictionary<int, float> _cardSeen = new Dictionary<int, float>();
        readonly List<int> _cardKeys = new List<int>();

        static string TierName(SabotageTier tier) => tier == SabotageTier.Chaos ? "MODO CAOS" : "TRAVESSURA";

        static string Remaining(ModView m)
        {
            string unit = m.DurationType == ModifierDurationType.Shots ? (m.Remaining == 1 ? "TACADA" : "TACADAS")
                : m.DurationType == ModifierDurationType.Rounds ? (m.Remaining == 1 ? "RONDA" : "RONDAS")
                : m.DurationType == ModifierDurationType.Turns ? (m.Remaining == 1 ? "TURNO" : "TURNOS") : "";
            return unit.Length == 0 ? "" : m.Remaining + " " + unit;
        }

        /// <summary>
        /// Uma carta por efeito activo do jogador local, empilhadas na coluna da direita: ícone, nome, categoria,
        /// descrição e duração restante (do estado replicado). Entram a deslizar e somem quando o efeito expira.
        /// Sem espaço para todas completas, as mais antigas ficam compactas (sem a descrição).
        /// </summary>
        void DrawDebuffCards(NetGame net, float top, float x, float w)
        {
            var me = net.LocalPlayer;
            var catalog = NetGame.SharedCatalog;
            if (me == null || catalog == null) return;
            float now = Time.unscaledTime;
            _cardKeys.Clear();
            foreach (var key in _cardSeen.Keys) _cardKeys.Add(key);
            foreach (int key in _cardKeys)
            {
                bool still = false;
                foreach (var m in me.Mods)
                    if (m.Def == key) still = true;
                if (!still) _cardSeen.Remove(key);
            }
            if (me.Mods.Count == 0) return;

            const float full = 128f, compact = 70f, gap = 10f;
            float bottomLimit = _vh - 240f;
            int fullCount = me.Mods.Count;
            while (fullCount > 0 && top + fullCount * (full + gap) + (me.Mods.Count - fullCount) * (compact + gap) > bottomLimit) fullCount--;
            float y = top;
            for (int i = me.Mods.Count - 1; i >= 0; i--)
            {
                var m = me.Mods[i];
                if (m.Def < 0 || m.Def >= catalog.Count) continue;
                var def = catalog[m.Def];
                if (!_cardSeen.TryGetValue(m.Def, out float born)) _cardSeen[m.Def] = born = now;
                float enter = Mathf.Clamp01((now - born) / 0.35f);
                float slide = (1f - EaseOutBack(enter)) * 60f;
                bool expanded = me.Mods.Count - 1 - i < fullCount;
                float h = expanded ? full : compact;
                if (y + h > _vh - 170f) break;
                var card = new Rect(x + slide, y, w, h);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, enter);
                Plate(card, Color.Lerp(NavyDark, def.Tint, 0.18f), false, 16f);
                // Ícone: disco na cor do efeito com o rótulo curto.
                var icon = new Rect(card.x + 12f, card.y + 10f, 50f, 50f);
                Round(icon, NavyDark, 25f);
                Round(Inset(icon, 4f), def.Tint, 21f);
                T(Inset(icon, 4f), def.ShortLabel.Length <= 3 ? def.ShortLabel : def.ShortLabel.Substring(0, 1), 22, NavyDark, TextAnchor.MiddleCenter, true);
                T(new Rect(card.x + 72f, card.y + 6f, w - 84f, 30f), def.DisplayName.ToUpperInvariant(), 20, Ivory, TextAnchor.MiddleLeft, true);
                bool chaos = def.Tier == SabotageTier.Chaos;
                Pill(new Rect(card.x + 72f, card.y + 38f, 124f, 24f), TierName(def.Tier), chaos ? Coral : Mustard, NavyDark, 13);
                string left = Remaining(m);
                if (left.Length > 0) T(new Rect(card.xMax - 150f, card.y + 36f, 138f, 28f), left, 17, Mustard, TextAnchor.MiddleRight, true);
                if (expanded)
                    T(new Rect(card.x + 14f, card.y + 68f, w - 28f, h - 74f), def.Description, 16, Ivory, TextAnchor.UpperLeft, false, false, true);
                GUI.color = old;
                y += h + gap;
            }
        }

        // ================================================================== frame de impacto da roleta

        void DrawImpactFrame()
        {
            var device = _game.Device;
            if (device == null) return;
            float age = Time.unscaledTime - device.ImpactAt;
            if (age < 0f || age > 0.5f) return;
            bool comfort = _game.Settings.ComfortMode;
            // 1) Flash curto e contido (nada de estroboscópio); 2) vinheta vermelha a sair; 3) "SPLAT!" em relevo.
            float flash = Mathf.Clamp01(1f - age / 0.1f) * (comfort ? 0.3f : 0.55f);
            if (flash > 0f) Fill(new Rect(0f, 0f, _vw, _vh), new Color(1f, 0.95f, 0.9f, flash));
            float red = Mathf.Clamp01(1f - age / 0.5f) * 0.45f;
            for (int i = 0; i < 6; i++)
            {
                float edge = 30f + i * 26f;
                var c = new Color(ArtPalette.Goo.r, ArtPalette.Goo.g * 0.5f, ArtPalette.Goo.b * 0.5f, red * (1f - i / 6f) * 0.5f);
                Fill(new Rect(0f, 0f, _vw, edge), c);
                Fill(new Rect(0f, _vh - edge, _vw, edge), c);
                Fill(new Rect(0f, 0f, edge, _vh), c);
                Fill(new Rect(_vw - edge, 0f, edge, _vh), c);
            }
        }

        // ================================================================== jukebox partilhada

        void DrawJukeboxPrompt(NetGame net)
        {
            var online = net.View.Music.Online.Current;
            var player = OnlineMusicPlayer.Instance;
            if (online == null || player == null || _game.Interaction.Active is JukeboxStation) return;
            bool needsEnable = !player.Enabled && !player.Failed;
            bool blocked = player.Enabled && player.AutoplayBlocked;
            if (!needsEnable && !blocked) return;
            float w = 620f;
            var r = new Rect(_vw - w - 24f, _vh - 250f, w, 64f);
            Plate(r, NavyDark, false, 18f, true);
            Icon(new Rect(r.x + 20f, r.y + 16f, 32f, 32f), IconKind.Music, Mustard);
            string title = OnlineMusic(online.Title);
            T(new Rect(r.x + 66f, r.y + 4f, w - 250f, 30f), "A SALA ESTÁ A OUVIR", 15, Muted, TextAnchor.MiddleLeft, true);
            T(new Rect(r.x + 66f, r.y + 28f, w - 250f, 30f), title, 19, Ivory, TextAnchor.MiddleLeft, true);
            string action = blocked ? "[J] ATIVAR SOM" : "[J] OUVIR TAMBÉM";
            if (Btn(new Rect(r.xMax - 176f, r.y + 10f, 164f, 44f), action, Mustard, NavyDark, true, 16))
            {
                if (blocked) player.RetryPlayback();
                else player.EnablePlayer();
            }
        }

        /// <summary>[J]: ouvir o YouTube da sala (consentimento lembrado) ou voltar a tentar se o autoplay foi bloqueado.</summary>
        void JukeboxKey(NetGame net)
        {
            var player = OnlineMusicPlayer.Instance;
            if (player == null || net.View.Music.Online.Current == null) return;
            if (player.Enabled && player.AutoplayBlocked) player.RetryPlayback();
            else if (!player.Enabled) player.EnablePlayer();
        }

        static string OnlineMusic(string title) => string.IsNullOrEmpty(title) ? "YouTube" : title.Length > 44 ? title.Substring(0, 43) + "…" : title;

        // ================================================================== marcadores

        void DrawPings()
        {
            var pings = _game.Pings;
            var cam = _game.Rig.Camera;
            if (pings == null || cam == null) return;
            foreach (var m in pings.Markers)
            {
                Vector3 sp = cam.WorldToScreenPoint(m.Point + Vector3.up * 0.55f);
                if (sp.z <= 0f || m.Alpha <= 0.01f) continue;
                var pos = new Vector2(sp.x / _scale, (UnityEngine.Screen.height - sp.y) / _scale);
                var r = new Rect(pos.x - 90f, pos.y - 30f, 180f, 26f);
                Round(r, new Color(0.03f, 0.05f, 0.09f, 0.55f * m.Alpha), 13f);
                var c = Color.Lerp(m.Color, Color.white, 0.35f);
                c.a = m.Alpha;
                T(r, string.IsNullOrEmpty(m.Name) ? "AQUI" : m.Name.ToUpperInvariant(), 15, c, TextAnchor.MiddleCenter, true);
            }
            // Aviso discreto junto à mira quando o marcador ainda está a recarregar.
            float left = pings.CooldownNoticeUntil - Time.unscaledTime;
            if (left > 0f)
            {
                var n = new Rect(_vw * 0.5f - 150f, _vh * 0.5f + 26f, 300f, 28f);
                float a = Mathf.Clamp01(left / 0.3f);
                Round(n, new Color(0.03f, 0.05f, 0.09f, 0.6f * a), 14f);
                float secs = Mathf.Max(0.1f, _bound != null ? _bound.PingCooldownRemaining : pings.CooldownNoticeSeconds);
                T(n, "MARCADOR EM RECARGA · " + secs.ToString("0.0") + " s", 15, new Color(Muted.r, Muted.g, Muted.b, a), TextAnchor.MiddleCenter, true);
            }
        }

        // ================================================================== modo de brincadeira

        void DrawFunMode(NetGame net)
        {
            var v = net.View;
            var winner = v.GetPlayer(v.MatchWinner);
            if (winner == null) return;
            bool shooter = net.LocalIsFunShooter;
            string title = shooter ? "MODO LIVRE · ÉS O VENCEDOR" : "MODO LIVRE · " + winner.Name.ToUpperInvariant() + " VENCEU";
            string body = shooter ? "Clique esquerdo dispara contra quem perdeu (só para rir — o resultado já está fechado)."
                : "Foge do vencedor! Um tiro deixa-te no chão por um segundo.";
            float w = Mathf.Min(760f, _vw - 80f);
            var r = new Rect((_vw - w) * 0.5f, 24f, w, 104f);
            Plate(r, NavyDark, true, 20f);
            T(new Rect(r.x, r.y + 8f, r.width, 44f), title, 30, Mustard, TextAnchor.MiddleCenter, true, true);
            T(new Rect(r.x + 24f, r.y + 52f, r.width - 48f, 44f), body + "  ·  [TAB] placar" + (net.IsLocalHost ? " e revanche" : ""), 18, Ivory, TextAnchor.MiddleCenter, false, false, true);
            if (shooter && !_game.CursorFree)
            {
                float cx = _vw * 0.5f, cy = _vh * 0.5f;
                Outline(new Rect(cx - 16f, cy - 16f, 32f, 32f), Coral, 2.5f, 16f);
            }
        }

        // ================================================================== layout responsivo

        /// <summary>
        /// Rectângulo para uma placa central no topo, entre a placa do jogador (esquerda) e o placar (direita). Se não
        /// houver largura suficiente (ecrãs estreitos ou interface grande), desce para baixo da coluna da esquerda.
        /// </summary>
        Rect CenterSlot(float desired, float min, float height)
        {
            float left = 24f + 520f + 16f;
            float right = _vw - 24f - 330f - 16f;
            if (right - left >= min)
            {
                float w = Mathf.Min(desired, right - left);
                float x = Mathf.Clamp((_vw - w) * 0.5f, left, right - w);
                return new Rect(x, 24f, w, height);
            }
            float w2 = Mathf.Min(desired, right - 24f);
            return new Rect(24f + (right - 24f - w2) * 0.5f, 352f, w2, height);
        }
    }
}
