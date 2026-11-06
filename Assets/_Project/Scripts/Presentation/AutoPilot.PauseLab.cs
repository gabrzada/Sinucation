using System.Collections.Generic;
using System.IO;
using System.Linq;
using FriendSlop.Billiards;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Laboratório da pausa contra bots em builds reais. No anfitrião (-fsPractice N -fsAutoplay -fsPauseLab):
    /// em cada cenário (intro de ronda, mira do humano e do bot, bolas a rolar, sorteio, gatilho, revelação, recarga da
    /// sabotagem, loja com -fsShopPhase, modo livre) pausa pelo caminho real (tecla P, Esc ou botão), espera 4 s, confirma
    /// que nada mexeu e que os comandos são recusados, retoma e confirma que tudo continua. Cada verificação fica no log
    /// como [GoalQA]. Entrada durante a pausa: anfitrião com -fsPauseJoin &lt;pasta&gt; e um cliente com
    /// -fsPauseJoiner &lt;pasta&gt; (ficheiros na pasta sincronizam os dois processos).
    /// </summary>
    public sealed partial class AutoPilot
    {
        sealed class PauseProbe
        {
            public MatchPhase Phase;
            public float Remaining, MatchTime, ViewRemaining;
            public int ShotSequence, Round, Selected, FunShots;
            public string RiskEventId;
            public readonly List<BallState> Balls = new List<BallState>();
            public AimState Aim;
            public readonly List<float> Cooldowns = new List<float>();
            public readonly List<Vector3> Bodies = new List<Vector3>();
            public Vector3 Hips;
        }

        static readonly string[] PauseScenarios = { "intro", "mira_humano", "mira_bot", "bolas_a_rolar", "sorteio", "gatilho", "revelacao", "sabotagem", "loja", "modo_livre" };

        readonly HashSet<string> _plDone = new HashSet<string>();
        string _plActive;
        int _plStep;
        float _plAt, _plStart;
        string _plMethod;
        int _plHipsTarget = -1;
        int _plSimFrames, _plFunShots, _plFrozenFrame = -1;
        float _plFunShotAt = -10f;
        int _plFunTarget = -1;
        bool _plHooked, _plCoinsGiven, _plMultiChecked;
        string _plPurchaseReply;
        PauseProbe _plBefore;
        // Entrada durante a pausa (anfitrião).
        int _pjStep;
        float _pjAt;

        bool PauseLabHost => Has("-fsPauseLab");

        /// <summary>Devolve true enquanto um cenário está a decorrer (o piloto automático não joga).</summary>
        bool UpdatePauseLab(NetGame net, MatchView view)
        {
            if (!net.IsLocalHost || net.ServerDirector == null) return false;
            var d = net.ServerDirector;
            if (!_plHooked)
            {
                _plHooked = true;
                net.FunShot += (shooter, target, from, at) => { _plFunShots++; _plFunShotAt = _time; _plFunTarget = target; };
                net.PurchaseCompleted += (ok, reason, cost) => _plPurchaseReply = (ok ? "aceite" : "recusada") + ": " + reason;
                Log("[PauseLab] cenários: " + string.Join(", ", PauseScenarios));
            }
            _plSimFrames = view.Phase == MatchPhase.Simulating ? _plSimFrames + 1 : 0;

            if (view.Players.Any(p => !p.IsBot && p.ClientId != net.LocalClientId) && _plActive == null && _pjStep == 0)
                return UpdatePauseMulti(net, view, d);
            if (Has("-fsPauseJoin") && UpdatePauseJoinHost(net, view, d)) return true;

            if (_plActive == null)
            {
                string start = PickPauseScenario(net, view, d);
                if (start == null) return false;
                BeginPauseScenario(net, view, d, start);
                return true;
            }
            RunPauseScenario(net, view, d);
            return true;
        }

        string PickPauseScenario(NetGame net, MatchView view, MatchDirector d)
        {
            var me = net.LocalPlayer;
            if (me != null && !_plCoinsGiven)
            {
                // Moedas para o piloto comprar sabotagens (a recarga é um dos relógios que a pausa tem de parar).
                _plCoinsGiven = true;
                d.GetPlayer(me.Id).Coins = 400;
            }
            bool Want(string key) => !_plDone.Contains(key);
            switch (view.Phase)
            {
                case MatchPhase.RoundIntro when Want("intro") && d.PhaseElapsed >= 0.6f:
                    return "intro";
                case MatchPhase.AwaitShot when Want("mira_humano") && me != null && view.CurrentPlayer == me.Id && _aiming && _actionTimer >= 1.6f:
                    return "mira_humano";
                case MatchPhase.AwaitShot when Want("mira_bot") && view.GetPlayer(view.CurrentPlayer)?.IsBot == true && net.RemoteAim.IsValid &&
                                               net.RemoteAim.PlayerId == view.CurrentPlayer && d.PhaseElapsed >= 1.5f:
                    return "mira_bot";
                case MatchPhase.AwaitShot when Want("sabotagem") && me != null && view.SabotageAnytime && d.Sabotage.CooldownFor(me.Id) > 3f:
                    return "sabotagem";
                case MatchPhase.Simulating when Want("bolas_a_rolar") && _plSimFrames >= 12 && d.Sim.IsSimulating:
                    return "bolas_a_rolar";
                case MatchPhase.PunishmentDraw when Want("sorteio") && d.PhaseElapsed >= 0.8f:
                    return "sorteio";
                case MatchPhase.AwaitTrigger when Want("gatilho") && d.PhaseElapsed >= 2f:
                    return "gatilho";
                case MatchPhase.RiskReveal when Want("revelacao") && d.PhaseElapsed >= 0.6f:
                    return "revelacao";
                case MatchPhase.Shop when Want("loja") && d.PhaseElapsed >= 1f:
                    return "loja";
                case MatchPhase.MatchOver when Want("modo_livre"):
                    return PickFunPause(net, view) ? "modo_livre" : null;
            }
            return null;
        }

        /// <summary>Modo livre: pausa logo a seguir a um tiro, com o alvo a meio do ragdoll.</summary>
        bool PickFunPause(NetGame net, MatchView view)
        {
            if (_overTime < 0f || _time - _overTime < 4f) return false;
            if (_game.Hud.ResultsOpen && _shots.Add("pl_fun_close")) _game.Hud.HandleTab();
            if (net.LocalIsFunShooter && _time - _plFunShotAt > 2.5f)
            {
                // Vencedor humano: dispara contra um bot (o caminho do clique) e pausa a meio da queda.
                var from = _game.Rig.Camera.transform.position;
                foreach (var p in view.Players)
                {
                    if (p.Id == view.MatchWinner) continue;
                    var avatar = AvatarLocator.Find(net, p.Id);
                    if (avatar == null) continue;
                    Vector3 center = avatar.transform.position + Vector3.up * 0.7f;
                    net.RequestFunShot(from, center - from, p.Id, center);
                    break;
                }
            }
            // Bot vencedor dispara sozinho de poucos em poucos segundos; o humano acabou de disparar acima.
            return _plFunTarget >= 0 && _time - _plFunShotAt > 0.12f && _time - _plFunShotAt < 0.3f;
        }

        void BeginPauseScenario(NetGame net, MatchView view, MatchDirector d, string key)
        {
            _plActive = key;
            _plStep = 0;
            _plStart = _time;
            _plFrozenFrame = -1;
            _plPurchaseReply = null;
            // Boneco cuja pose tem de ficar congelada: o bot a apontar, o bot com o revólver ou o alvo do ragdoll.
            _plHipsTarget = -1;
            if (key == "modo_livre") _plHipsTarget = _plFunTarget;
            else if (key == "mira_bot") _plHipsTarget = view.CurrentPlayer;
            else if (key == "gatilho" && view.GetPlayer(view.Selected)?.IsBot == true) _plHipsTarget = view.Selected;
            // Três caminhos reais, alternados: tecla P, Esc (o menu de pausa congela a partida) e o botão/atalho.
            _plMethod = key == "intro" || key == "bolas_a_rolar" || key == "loja" ? "esc" : key == "mira_humano" || key == "gatilho" ? "tecla_p" : "botao";
            _plBefore = null;
            // O Esc fecha primeiro o ecrã "ESTÁS FORA" (como no jogo); o laboratório fecha-o antes, para o Esc abrir a pausa.
            if (_plMethod == "esc" && _game.Hud.EliminatedOverlayOpen) _game.Hud.HandleBack();
            Log("[PauseLab] " + key + ": a pausar por " + _plMethod + " (fase " + view.Phase + ", resta " + d.PhaseTimeRemaining.ToString("0.00") + " s)");
            if (_plMethod == "tecla_p") PressKey(Key.P);
            else if (_plMethod == "esc") PressKey(Key.Escape);
            else _game.ToggleMatchPause();
        }

        void PressKey(Key key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            _plReleaseKey = true;
        }

        bool _plReleaseKey;

        void RunPauseScenario(NetGame net, MatchView view, MatchDirector d)
        {
            if (_plReleaseKey && Keyboard.current != null)
            {
                _plReleaseKey = false;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                return;
            }
            string key = _plActive;
            switch (_plStep)
            {
                case 0:
                    // Espera que o anfitrião aplique a pausa (o RPC corre no próprio processo).
                    // A autoridade pára no próprio frame; o tempo local (timeScale) só no frame seguinte, porque o deltaTime
                    // de um frame já está decidido quando ele começa. Mede-se a partir do primeiro frame já congelado.
                    if (!d.Paused || Time.timeScale > 0f)
                    {
                        if (_time - _plStart > 1.5f) EndPauseScenario(false, "a pausa não chegou a entrar (" + _plMethod + ")");
                        return;
                    }
                    if (_plFrozenFrame < 0) _plFrozenFrame = Time.frameCount;
                    if (Time.frameCount <= _plFrozenFrame) return;
                    _plBefore = ProbePause(net, view, d);
                    _plStep = 1;
                    _plAt = _time;
                    return;
                case 1:
                    if (_time - _plAt < 0.3f) return;
                    TryCommandsWhilePaused(net, view, d);
                    Schedule("pausa_" + key, 1.2f, true);
                    _plStep = 2;
                    return;
                case 2:
                    if (_time - _plAt < 4f) return;
                    var now = ProbePause(net, view, d);
                    CheckFrozen(key, _plBefore, now, net, view, d);
                    // Retoma pelo mesmo caminho por que pausou.
                    if (_plMethod == "tecla_p") PressKey(Key.P);
                    else if (_plMethod == "esc") PressKey(Key.Escape);
                    else _game.ToggleMatchPause();
                    _plStep = 3;
                    _plAt = _time;
                    return;
                case 3:
                    if (d.Paused && _time - _plAt < 1.5f) return;
                    GoalCheck(!d.Paused && !_game.Paused, "pausa/" + key + ": retomou por " + _plMethod + " (menu fechado=" + !_game.Paused + ")");
                    _plStep = 4;
                    _plAt = _time;
                    return;
                case 4:
                    if (_time - _plAt < 1.2f) return;
                    CheckResumed(key, _plBefore, ProbePause(net, view, d), d);
                    EndPauseScenario(true, null);
                    return;
            }
        }

        void EndPauseScenario(bool ok, string failure)
        {
            if (!ok) GoalCheck(false, "pausa/" + _plActive + ": " + failure);
            _plDone.Add(_plActive);
            Log("[PauseLab] " + _plActive + " terminado; feitos: " + string.Join(",", _plDone) + " · faltam: " + string.Join(",", PauseScenarios.Where(s => !_plDone.Contains(s))));
            if (_game.MatchPaused) _game.ToggleMatchPause();
            if (_game.Paused) _game.SetPauseMenu(false, true);
            _plActive = null;
        }

        PauseProbe ProbePause(NetGame net, MatchView view, MatchDirector d)
        {
            var p = new PauseProbe
            {
                Phase = d.Phase,
                Remaining = d.PhaseTimeRemaining,
                ViewRemaining = view.PhaseRemaining,
                MatchTime = d.MatchTime,
                ShotSequence = d.ShotSequence,
                Round = d.RoundIndex,
                Selected = d.SelectedPlayerId,
                RiskEventId = d.RiskEventId,
                Aim = net.RemoteAim,
                FunShots = _plFunShots
            };
            d.Sim.CopyState(p.Balls);
            foreach (var pl in d.Players) p.Cooldowns.Add(d.Sabotage.CooldownFor(pl.Id));
            foreach (var pl in view.Players)
            {
                var avatar = AvatarLocator.Find(net, pl.Id);
                p.Bodies.Add(avatar != null ? avatar.transform.position : Vector3.zero);
            }
            var hipsOwner = _plHipsTarget >= 0 ? AvatarLocator.Find(net, _plHipsTarget) : null;
            p.Hips = hipsOwner != null && hipsOwner.Hips != null ? hipsOwner.Hips.position : Vector3.zero;
            return p;
        }

        readonly Dictionary<int, (Vector3 holder, Vector3 visual, Vector3 hips)> _plTrace = new Dictionary<int, (Vector3, Vector3, Vector3)>();
        int _plTraceLogs;
        string _plTraceKey;

        /// <summary>Diagnóstico: com a partida pausada, regista o frame em que algum boneco se mexe e o tempo desse frame.</summary>
        void TracePauseBodies(NetGame net)
        {
            if (_plTraceKey != _plActive)
            {
                _plTraceKey = _plActive;
                _plTrace.Clear();
                _plTraceLogs = 0;
            }
            foreach (var p in net.View.Players)
            {
                var avatar = AvatarLocator.Find(net, p.Id);
                if (avatar == null) continue;
                var now = (avatar.transform.parent != null ? avatar.transform.parent.position : Vector3.zero, avatar.transform.position, avatar.Hips != null ? avatar.Hips.position : Vector3.zero);
                if (_plTrace.TryGetValue(p.Id, out var last) && _plTraceLogs < 8 &&
                    ((now.Item1 - last.holder).sqrMagnitude > 1e-8f || (now.Item2 - last.visual).sqrMagnitude > 1e-8f || (now.Item3 - last.hips).sqrMagnitude > 1e-8f))
                {
                    _plTraceLogs++;
                    Log("[PauseTrace] " + _plActive + " passo " + _plStep + " frame " + Time.frameCount + " (congelado em " + _plFrozenFrame + ") dt=" + Time.deltaTime.ToString("0.0000") + " escala=" + Time.timeScale +
                        " jogador " + p.Id + (p.IsBot ? " bot" : " humano") + " suporte Δ" + ((now.Item1 - last.holder).magnitude * 1000f).ToString("0.00") +
                        " mm, boneco Δ" + ((now.Item2 - last.visual).magnitude * 1000f).ToString("0.00") + " mm, anca Δ" + ((now.Item3 - last.hips).magnitude * 1000f).ToString("0.00") +
                        " mm, ragdoll=" + avatar.InRagdoll + "/" + avatar.Recovering);
                }
                _plTrace[p.Id] = now;
            }
        }

        void TryCommandsWhilePaused(NetGame net, MatchView view, MatchDirector d)
        {
            var me = net.LocalPlayer;
            if (me == null || me.Eliminated) return;
            // Tudo pelo caminho de rede normal (RPC → anfitrião), como faria um clique.
            if (view.Phase == MatchPhase.AwaitShot && view.CurrentPlayer == me.Id)
                net.RequestShot(new Vector2(1f, 0f), 0.6f, Vector2.zero);
            if (view.Phase == MatchPhase.AwaitTrigger && view.Selected == me.Id)
                net.RequestTrigger();
            if (view.Phase == MatchPhase.Shop || view.SabotageAnytime)
            {
                var target = view.Players.FirstOrDefault(p => p.Id != me.Id && !p.Eliminated);
                if (target != null) net.RequestPurchase(target.Id, 0);
            }
            if (view.Phase == MatchPhase.Shop) net.RequestPassShop();
            if (net.LocalIsFunShooter)
            {
                var other = view.Players.FirstOrDefault(p => p.Id != me.Id);
                var avatar = other != null ? AvatarLocator.Find(net, other.Id) : null;
                if (avatar != null)
                {
                    var from = _game.Rig.Camera.transform.position;
                    Vector3 center = avatar.transform.position + Vector3.up * 0.7f;
                    net.RequestFunShot(from, center - from, other.Id, center);
                }
            }
        }

        void CheckFrozen(string key, PauseProbe a, PauseProbe b, NetGame net, MatchView view, MatchDirector d)
        {
            string tag = "pausa/" + key + ": ";
            GoalCheck(d.Paused && view.MatchPaused, tag + "partida pausada no anfitrião e no snapshot");
            GoalCheck(Mathf.Approximately(Time.timeScale, 0f), tag + "tempo local congelado (timeScale " + Time.timeScale + ")");
            bool shown = _game.Paused || Time.frameCount - _game.Hud.PauseBannerFrame < 5;
            GoalCheck(shown, tag + "indicação no ecrã (" + (_game.Paused ? "menu PAUSA" : "aviso PARTIDA EM PAUSA") + ")");
            GoalCheck(a.Phase == b.Phase && a.Remaining == b.Remaining && a.ViewRemaining == b.ViewRemaining,
                tag + "fase e relógio parados (" + a.Phase + " " + a.Remaining.ToString("0.000") + " → " + b.Phase + " " + b.Remaining.ToString("0.000") + ", HUD " + b.ViewRemaining.ToString("0.000") + ")");
            GoalCheck(a.MatchTime == b.MatchTime, tag + "tempo de jogo parado (" + a.MatchTime.ToString("0.000") + " → " + b.MatchTime.ToString("0.000") + ")");
            float ballMove = 0f;
            for (int i = 0; i < Mathf.Min(a.Balls.Count, b.Balls.Count); i++) ballMove = Mathf.Max(ballMove, (a.Balls[i].Position - b.Balls[i].Position).magnitude);
            GoalCheck(ballMove == 0f && a.Balls.Count == b.Balls.Count, tag + "bolas paradas (máx. " + (ballMove * 1000f).ToString("0.000") + " mm)");
            GoalCheck(a.ShotSequence == b.ShotSequence && a.Round == b.Round && a.Selected == b.Selected && a.RiskEventId == b.RiskEventId,
                tag + "nenhuma tacada/ronda/gatilho durante a pausa (tacada " + a.ShotSequence + "→" + b.ShotSequence + ", ronda " + a.Round + "→" + b.Round + ")");
            GoalCheck(a.Aim.PlayerId == b.Aim.PlayerId && a.Aim.Yaw == b.Aim.Yaw && a.Aim.Pull == b.Aim.Pull,
                tag + "mira do bot congelada (jogador " + b.Aim.PlayerId + ", yaw " + b.Aim.Yaw.ToString("0.00") + ", puxar " + b.Aim.Pull.ToString("0.00") + ")");
            bool cooldowns = a.Cooldowns.SequenceEqual(b.Cooldowns);
            GoalCheck(cooldowns, tag + "recargas da sabotagem paradas (" + string.Join("/", b.Cooldowns.Select(c => c.ToString("0.00"))) + ")");
            float bodyMove = 0f;
            for (int i = 0; i < Mathf.Min(a.Bodies.Count, b.Bodies.Count); i++) bodyMove = Mathf.Max(bodyMove, (a.Bodies[i] - b.Bodies[i]).magnitude);
            GoalCheck(bodyMove < 0.001f, tag + "bonecos parados (máx. " + (bodyMove * 1000f).ToString("0.0") + " mm)");
            for (int i = 0; i < Mathf.Min(a.Bodies.Count, b.Bodies.Count) && i < view.Players.Count; i++)
                if ((a.Bodies[i] - b.Bodies[i]).sqrMagnitude > 1e-6f)
                    Log("[PauseLab] " + key + ": jogador " + view.Players[i].Id + (view.Players[i].IsBot ? " (bot)" : " (humano)") + " mexeu " +
                        a.Bodies[i].ToString("F4") + " → " + b.Bodies[i].ToString("F4"));
            if (_plHipsTarget >= 0)
                GoalCheck((a.Hips - b.Hips).magnitude < 0.001f, tag + "pose do jogador " + _plHipsTarget + " congelada (anca " + ((a.Hips - b.Hips).magnitude * 1000f).ToString("0.0") + " mm)");
            GoalCheck(a.FunShots == b.FunShots, tag + "nenhum tiro do modo livre durante a pausa (" + a.FunShots + "→" + b.FunShots + ")");
            if (_plPurchaseReply != null)
                GoalCheck(_plPurchaseReply.Contains("pausa"), tag + "compra durante a pausa recusada (" + _plPurchaseReply + ")");
        }

        void CheckResumed(string key, PauseProbe a, PauseProbe b, MatchDirector d)
        {
            string tag = "pausa/" + key + ": ";
            GoalCheck(Mathf.Approximately(Time.timeScale, 1f), tag + "tempo local volta ao normal (timeScale " + Time.timeScale + ")");
            if (a.Phase == MatchPhase.MatchOver)
            {
                // O relógio da partida acabou com ela; no modo livre continua a animação e os tiros.
                if (_plHipsTarget >= 0) GoalCheck((a.Hips - b.Hips).magnitude > 0.002f || b.FunShots > a.FunShots, tag + "o ragdoll/modo livre continua depois de retomar (anca " + ((a.Hips - b.Hips).magnitude * 1000f).ToString("0") + " mm)");
                return;
            }
            bool advanced = b.MatchTime > a.MatchTime + 0.5f;
            bool progressed = b.Phase != a.Phase || b.Remaining < a.Remaining || b.ShotSequence != a.ShotSequence || b.Round != a.Round || a.Phase == MatchPhase.Simulating;
            GoalCheck(advanced && progressed, tag + "continua de onde parou (tempo de jogo " + a.MatchTime.ToString("0.00") + " → " + b.MatchTime.ToString("0.00") +
                ", " + a.Phase + " " + a.Remaining.ToString("0.00") + " → " + b.Phase + " " + b.Remaining.ToString("0.00") + ")");
            if (a.Phase == MatchPhase.Simulating)
            {
                float move = 0f;
                for (int i = 0; i < Mathf.Min(a.Balls.Count, b.Balls.Count); i++) move = Mathf.Max(move, (a.Balls[i].Position - b.Balls[i].Position).magnitude);
                GoalCheck(move > 0.001f, tag + "as bolas voltam a rolar (" + (move * 1000f).ToString("0") + " mm)");
            }
            bool cooling = false, falling = true;
            for (int i = 0; i < Mathf.Min(a.Cooldowns.Count, b.Cooldowns.Count); i++)
            {
                if (a.Cooldowns[i] <= 1.5f) continue;
                cooling = true;
                falling &= b.Cooldowns[i] < a.Cooldowns[i];
            }
            if (cooling) GoalCheck(falling, tag + "as recargas da sabotagem voltam a descer (" + string.Join("/", b.Cooldowns.Select(c => c.ToString("0.00"))) + ")");
        }

        /// <summary>Partida com outro humano sentado: a pausa não existe (nem tecla, nem menu, nem RPC directo).</summary>
        bool UpdatePauseMulti(NetGame net, MatchView view, MatchDirector d)
        {
            if (_plMultiChecked || view.Phase != MatchPhase.AwaitShot || d.PhaseElapsed < 1f) return false;
            switch (_plStep)
            {
                case 0:
                    GoalCheck(!view.PauseAllowed && !net.CanPauseMatch, "pausa/2 humanos: recurso escondido (PauseAllowed=" + view.PauseAllowed + ")");
                    PressKey(Key.P);
                    _plStep = 1;
                    _plAt = _time;
                    return true;
                case 1:
                    if (_plReleaseKey) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); _plReleaseKey = false; }
                    if (_time - _plAt < 0.6f) return true;
                    GoalCheck(!d.Paused, "pausa/2 humanos: tecla P não pausa");
                    _game.SetPauseMenu(true, true);
                    net.RequestMatchPause(true);
                    _plStep = 2;
                    _plAt = _time;
                    Schedule("pausa_2humanos_menu", 0.5f, true);
                    return true;
                case 2:
                    if (_time - _plAt < 1.2f) return true;
                    GoalCheck(!d.Paused && !view.MatchPaused && Mathf.Approximately(Time.timeScale, 1f), "pausa/2 humanos: menu Esc e pedido directo não congelam a partida");
                    _game.SetPauseMenu(false, true);
                    _plMultiChecked = true;
                    _plStep = 0;
                    Log("[PauseLab] verificação com 2 humanos terminada");
                    return false;
            }
            return false;
        }

        // ------------------------------------------------------------------ entrada durante a pausa

        string PauseFlag(string name) => Path.Combine(Arg(Has("-fsPauseJoin") ? "-fsPauseJoin" : "-fsPauseJoiner"), name);

        /// <summary>Anfitrião: pausa, espera que o cliente seja recusado, retoma, deixa-o entrar e confirma que a pausa desaparece.</summary>
        bool UpdatePauseJoinHost(NetGame net, MatchView view, MatchDirector d)
        {
            switch (_pjStep)
            {
                case 0:
                    if (!_plDone.Contains("mira_bot") || _plActive != null || view.Phase != MatchPhase.AwaitShot) return false;
                    Directory.CreateDirectory(Arg("-fsPauseJoin"));
                    _game.ToggleMatchPause();
                    _pjStep = 1;
                    _pjAt = _time;
                    return true;
                case 1:
                    if (!d.Paused) { if (_time - _pjAt > 2f) { GoalCheck(false, "entrada: a pausa não entrou"); _pjStep = 9; } return true; }
                    File.WriteAllText(PauseFlag("paused.flag"), "1");
                    Log("[PauseLab] entrada: partida pausada; à espera da tentativa do cliente");
                    _pjStep = 2;
                    _pjAt = _time;
                    return true;
                case 2:
                    if (net.RefusedJoins == 0 && _time - _pjAt < 90f) return true;
                    GoalCheck(net.RefusedJoins > 0, "entrada: tentativa de entrar durante a pausa recusada pelo anfitrião (" + net.RefusedJoins + ")");
                    GoalCheck(d.Paused && _game.Network.ConnectedClientsIds.Count == 1, "entrada: a pausa mantém-se e ninguém ficou ligado (" + _game.Network.ConnectedClientsIds.Count + " ligações)");
                    Schedule("pausa_entrada_recusada", 0.2f, true);
                    _pjStep = 3;
                    _pjAt = _time;
                    return true;
                case 3:
                    if (_time - _pjAt < 2f) return true;
                    _game.ToggleMatchPause();
                    File.WriteAllText(PauseFlag("resumed.flag"), "1");
                    Log("[PauseLab] entrada: retomada; o cliente pode tentar de novo");
                    _pjStep = 4;
                    _pjAt = _time;
                    return true;
                case 4:
                    if (_game.Network.ConnectedClientsIds.Count < 2 && _time - _pjAt < 90f) return true;
                    if (_time - _pjAt < 4f) return true;
                    GoalCheck(_game.Network.ConnectedClientsIds.Count == 2, "entrada: depois de retomar o cliente entra (espectador)");
                    GoalCheck(!view.PauseAllowed && !net.CanPauseMatch, "entrada: com um espectador humano a pausa fica escondida");
                    _game.ToggleMatchPause();
                    net.RequestMatchPause(true);
                    _game.SetPauseMenu(true, true);
                    Schedule("pausa_espectador_menu", 0.5f, true);
                    _pjStep = 5;
                    _pjAt = _time;
                    return true;
                case 5:
                    if (_time - _pjAt < 1.5f) return true;
                    GoalCheck(!d.Paused && Mathf.Approximately(Time.timeScale, 1f), "entrada: com um espectador ligado, P/menu/RPC não pausam");
                    _game.SetPauseMenu(false, true);
                    File.WriteAllText(PauseFlag("checked.flag"), "1");
                    _pjStep = 6;
                    _pjAt = _time;
                    return true;
                case 6:
                    if (_game.Network.ConnectedClientsIds.Count > 1 && _time - _pjAt < 30f) return true;
                    if (_time - _pjAt < 2f) return true;
                    GoalCheck(view.PauseAllowed && net.CanPauseMatch, "entrada: o espectador saiu e a pausa volta a estar disponível");
                    _pjStep = 9;
                    return false;
            }
            return false;
        }

        int _pjoinStep;
        float _pjoinAt;

        /// <summary>Cliente: só tenta entrar quando o anfitrião está em pausa; depois de recusado, volta a tentar quando ele retoma.</summary>
        bool UpdatePauseJoiner()
        {
            switch (_pjoinStep)
            {
                case 0:
                    if (!File.Exists(PauseFlag("paused.flag"))) return true;
                    Log("[PauseLab] cliente: o anfitrião está em pausa — a tentar entrar");
                    StartSession();
                    _pjoinStep = 1;
                    _pjoinAt = _time;
                    return true;
                case 1:
                    if (_game.Screen != AppScreen.Menu || _time - _pjoinAt < 1f)
                    {
                        if (_time - _pjoinAt > 30f) { GoalCheck(false, "cliente: entrou durante a pausa (devia ser recusado)"); _pjoinStep = 3; }
                        return true;
                    }
                    string reason = _game.StatusMessage ?? "";
                    GoalCheck(reason.Contains("pausa"), "cliente: entrada recusada com motivo claro: \"" + reason + "\"");
                    Capture("pausa_cliente_recusado");
                    _pjoinStep = 2;
                    return true;
                case 2:
                    if (!File.Exists(PauseFlag("resumed.flag"))) return true;
                    Log("[PauseLab] cliente: o anfitrião retomou — nova tentativa");
                    StartSession();
                    _pjoinStep = 3;
                    _pjoinAt = _time;
                    return true;
                case 3:
                    var net = NetGame.Instance;
                    if (net == null || !net.HasSnapshot || _game.Screen != AppScreen.Session)
                    {
                        if (_time - _pjoinAt > 40f) { GoalCheck(false, "cliente: não conseguiu entrar depois de o anfitrião retomar"); Quit("entrada falhou"); }
                        return true;
                    }
                    GoalCheck(net.LocalPlayer == null && !net.CanPauseMatch, "cliente: entrou a meio como espectador, sem acesso à pausa");
                    _pjoinStep = 4;
                    return true;
                case 4:
                    if (!File.Exists(PauseFlag("checked.flag"))) return true;
                    Quit("entrada durante a pausa verificada");
                    _pjoinStep = 5;
                    return true;
            }
            return true;
        }
    }
}
