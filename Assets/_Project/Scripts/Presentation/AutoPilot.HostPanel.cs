using System.Linq;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// QA do painel do anfitrião e dos debuffs acumulativos em builds reais (-fsPanelTest, anfitrião e cliente):
    /// tempo por tacada, regras a meio da partida (só na ronda seguinte), subir à mesa, pular música bloqueado,
    /// quatro debuffs ao mesmo tempo no cliente e reinício da partida. Cada verificação fica no log como [GoalQA].
    /// </summary>
    public sealed partial class AutoPilot
    {
        int _panelStep, _panelRoundAtRules = -1, _panelLastMods = -1;
        float _panelAt, _panelLogAt, _panelClimbOffAt = -1f;
        bool _panelTurnChecked, _panelSkipTried, _panelClimbChecked, _panelResetSeen;
        float _panelUiOpenedAt = -1f;
        int _panelUiFrame;

        static readonly string[] PanelDebuffs = { ModifierCatalog.SpiderMode, ModifierCatalog.Myopia, ModifierCatalog.ColorBlindness, ModifierCatalog.TunnelVision };

        void UpdatePanelTest(NetGame net)
        {
            if (!Has("-fsPanelTest") || !net.HasSnapshot) return;
            var v = net.View;
            if (net.IsServer) UpdatePanelHost(net, v);
            else UpdatePanelClient(net, v);

            if (_time >= _panelLogAt)
            {
                _panelLogAt = _time + 4f;
                var me = net.LocalPlayer;
                Log("painel: ronda=" + v.RoundIndex + " fase=" + v.Phase + " tempo=" + v.PhaseDuration.ToString("0") + "/" + v.Settings.TurnSeconds +
                    " subir=" + v.Settings.AllowTableClimb + " barreira=" + (RoomBuilder.PlayerTableBarrier != null && RoomBuilder.PlayerTableBarrier.enabled) +
                    " pular_bloqueado=" + v.Music.Online.SkipLocked + " efeitos=" + (me != null ? string.Join(",", me.Mods.Select(m => NetGame.SharedCatalog[m.Def].Id + ":" + m.Remaining)) : "-"));
            }
        }

        void UpdatePanelHost(NetGame net, MatchView v)
        {
            var d = net.ServerDirector;
            if (d == null || !v.InMatch) return;
            // 1) Tempo por tacada por omissão: 20 s.
            if (!_panelTurnChecked && v.Phase == MatchPhase.AwaitShot)
            {
                _panelTurnChecked = true;
                GoalCheck(Mathf.Approximately(v.PhaseDuration, 20f), "tempo por tacada de 20 s (vale " + v.PhaseDuration + ")");
            }
            // 0) O botão da pausa abre o painel a meio da partida e ele fica aberto (antes fechava-se no frame seguinte).
            if (_panelUiOpenedAt < 0f && _time > 15f && v.Phase != MatchPhase.MatchOver)
            {
                _game.Hud.OpenHostPanel();
                _panelUiOpenedAt = _time;
                _panelUiFrame = Time.frameCount;
                Log("painel: anfitrião abre PAINEL DO ANFITRIÃO pela pausa (ronda " + v.RoundIndex + ", fase " + v.Phase + ")");
                Schedule("painel_aberto", 0.6f, true);
            }
            if (_panelUiOpenedAt >= 0f && _time > _panelUiOpenedAt + 1.5f && _shots.Add("goal_painel_ui"))
            {
                GoalCheck(_game.Hud.RoomSettingsOpen, "painel do anfitrião continua aberto " + (Time.frameCount - _panelUiFrame) + " frames depois de abrir a meio da partida");
                _game.Hud.HandleBack();
            }
            var client = v.Players.FirstOrDefault(p => !p.IsBot && p.ClientId != net.LocalClientId);
            if (client == null) return;
            switch (_panelStep)
            {
                case 0 when _time > 25f:
                    // 2) Regras a meio da partida (15 s por tacada) + subir à mesa ligado.
                    var rules = v.Settings.Copy();
                    rules.TurnSeconds = 15;
                    rules.AllowTableClimb = true;
                    net.HostApplySettings(rules);
                    _panelRoundAtRules = v.RoundIndex;
                    Log("painel: anfitrião mudou para 15 s por tacada e ligou subir à mesa (ronda " + v.RoundIndex + ")");
                    _panelStep = 1;
                    break;
                case 1:
                    // Regras novas só na ronda seguinte; a actual mantém os 20 s.
                    if (v.Phase == MatchPhase.AwaitShot && v.RoundIndex == _panelRoundAtRules)
                        GoalCheckOnce("ronda_actual", Mathf.Approximately(v.PhaseDuration, 20f), "a ronda em curso mantém 20 s depois da mudança (" + v.PhaseDuration + ")");
                    if (v.Phase == MatchPhase.AwaitShot && v.RoundIndex > _panelRoundAtRules)
                    {
                        GoalCheckOnce("ronda_seguinte", Mathf.Approximately(v.PhaseDuration, 15f), "a ronda seguinte usa 15 s (" + v.PhaseDuration + ")");
                        _panelStep = 2;
                        _panelAt = _time + 3f;
                    }
                    break;
                case 2 when _time > _panelAt:
                    // 3) Pular música só para o anfitrião.
                    if (!v.Music.Online.SkipLocked) net.ControlOnlineMusic(OnlineMusicCommand.SkipLock);
                    // 4) Quatro debuffs de naturezas diferentes no cliente, como se viessem de travessuras.
                    var target = d.GetPlayer(client.Id);
                    int applied = 0;
                    foreach (var id in PanelDebuffs)
                    {
                        var def = NetGame.SharedCatalog.First(x => x.Id == id);
                        if (target.Modifiers.Apply(def, target.Id, -1, "QA-" + id, d.RoundIndex, 90000 + applied, d.Config.MaxGameplayModifiersPerPlayer) != null) applied++;
                    }
                    GoalCheck(applied == PanelDebuffs.Length && target.Modifiers.Active.Count >= PanelDebuffs.Length, "4 debuffs activos ao mesmo tempo no servidor (" + target.Modifiers.Active.Count + ")");
                    _panelStep = 3;
                    _panelAt = _time + 40f;
                    break;
                case 3 when _time > _panelAt:
                    var rules2 = v.Settings.Copy();
                    rules2.AllowTableClimb = false;
                    net.HostApplySettings(rules2);
                    Log("painel: anfitrião desligou subir à mesa");
                    _panelStep = 4;
                    _panelAt = _time + 8f;
                    break;
                case 4 when _time > _panelAt:
                    // 5) Reinício da partida.
                    Log("painel: anfitrião reinicia a partida (ronda " + v.RoundIndex + ")");
                    net.HostResetMatch();
                    _panelStep = 5;
                    _panelAt = _time + 3f;
                    break;
                case 5 when _time > _panelAt:
                    GoalCheck(v.RoundIndex == 1 && v.Players.All(p => p.Mods.Count == 0 && p.Coins == 0), "reinício: ronda 1, saldos e efeitos limpos (ronda " + v.RoundIndex + ")");
                    _panelStep = 6;
                    break;
            }
        }

        void UpdatePanelClient(NetGame net, MatchView v)
        {
            var me = net.LocalPlayer;
            var local = NetPlayer.Local;
            // Pular bloqueado: o cliente tenta e o servidor recusa (a fila não muda).
            if (v.Music.Online.SkipLocked && !_panelSkipTried)
            {
                _panelSkipTried = true;
                net.MusicNotice += text => Log("painel: aviso ao cliente: " + text);
                GoalCheck(!net.CanSkipMusic, "cliente vê o pular música bloqueado");
                net.ControlRadio(1);
                net.ControlOnlineMusic(OnlineMusicCommand.Next);
            }
            // Debuffs no cliente: todos sincronizados e compostos (um não desliga outro).
            if (me != null && me.Mods.Count != _panelLastMods)
            {
                _panelLastMods = me.Mods.Count;
                var list = me.Mods.Select(m => new ModifierInstance { Definition = NetGame.SharedCatalog[m.Def], Intensity = m.Intensity }).ToList();
                var profile = CameraModifierPipeline.Build(list, false);
                Log("painel: cliente com " + me.Mods.Count + " efeitos · rolo=" + profile.Roll + " miopia=" + profile.Myopia + " cores=" + profile.Deuteranopia + " túnel=" + profile.Vignette);
                if (me.Mods.Count >= PanelDebuffs.Length)
                    GoalCheck(profile.Roll == 90f && profile.Myopia > 0f && profile.Deuteranopia > 0f && profile.Vignette > 0f, "cliente: aranha + miopia + daltonismo + túnel compostos");
            }
            if (me != null && me.Mods.Count >= 2 && v.CurrentPlayer == me.Id && v.Phase == MatchPhase.AwaitShot) Schedule("debuffs_juntos", 1.5f, true);
            // Subir à mesa: posto em cima do tampo fica lá; com a regra desligada é empurrado para fora.
            if (local != null && v.Settings.AllowTableClimb && !_panelClimbChecked && v.CurrentPlayer != (me?.Id ?? -1))
            {
                _panelClimbChecked = true;
                local.TeleportTo(new Vector3(0.3f, TableView.TopY + 0.02f, 0.1f), 0f);
                _panelAt = _time + 2f;
            }
            if (_panelClimbChecked && _panelAt > 0f && _time > _panelAt && local != null)
            {
                _panelAt = -1f;
                var p = local.transform.position;
                GoalCheck(Mathf.Abs(p.x) < 1.1f && Mathf.Abs(p.z) < 0.55f && p.y > TableView.TopY - 0.1f, "cliente fica em cima da mesa com subir ligado (" + p + ")");
                Schedule("em_cima_da_mesa", 0.1f, true);
            }
            if (_panelClimbChecked && !v.Settings.AllowTableClimb && _panelClimbOffAt < 0f) _panelClimbOffAt = _time + 2f;
            if (_panelClimbOffAt > 0f && _time > _panelClimbOffAt && !_panelResetSeen && local != null)
            {
                _panelResetSeen = true;
                var p = local.transform.position;
                bool outside = Mathf.Abs(p.x) > 1.12f + RoomBuilder.TableColliderMargin || Mathf.Abs(p.z) > 0.56f + RoomBuilder.TableColliderMargin;
                GoalCheck(outside || p.y < TableView.TopY - 0.1f, "subir desligado: cliente sai de cima da mesa (" + p + ")");
            }
        }

        void GoalCheckOnce(string key, bool ok, string message)
        {
            if (!_shots.Add("goal_" + key)) return;
            GoalCheck(ok, message);
        }
    }
}
