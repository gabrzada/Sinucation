using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Piloto automático para testes de rede em builds reais (activado só por argumentos -fs*).
    /// Joga pelos mesmos comandos de rede que um humano, tira screenshots e escreve um relatório final
    /// para comparar se todos os clientes chegaram ao mesmo resultado (NFR-001).
    /// Ex.: -fsHost 7790 -fsBots 1 -fsStartWhen 4 -fsAutoplay -fsReport host.json
    ///      -fsJoin 127.0.0.1:7790 -fsAutoplay -fsReport c1.json -fsShots shots
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            Active = false;
        }

        public static bool Active { get; private set; }

        readonly Dictionary<string, string> _args = new Dictionary<string, string>();
        readonly HashSet<string> _shots = new HashSet<string>();
        readonly System.Random _rng = new System.Random(Environment.TickCount);
        GameBootstrap _game;
        float _time;
        float _actionTimer;
        int _actedKey = int.MinValue;
        bool _started;
        bool _reported;
        bool _settingsSent;
        float _overTime = -1f;
        int _aimShots;
        string _pendingShot;
        float _pendingShotAt;

        public static bool HasArguments()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith("-fs", StringComparison.Ordinal)) return true;
            return false;
        }

        void Awake()
        {
            Active = true;
            _game = GetComponent<GameBootstrap>();
            var argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                if (!argv[i].StartsWith("-fs", StringComparison.Ordinal)) continue;
                string value = i + 1 < argv.Length && !argv[i + 1].StartsWith("-", StringComparison.Ordinal) ? argv[i + 1] : "";
                _args[argv[i]] = value;
            }
            if (_args.TryGetValue("-fsName", out string name) && !string.IsNullOrEmpty(name))
            {
                _game.Settings.PlayerName = name;
                // Cada instância de teste com forma/cor diferentes (as PlayerPrefs são partilhadas na mesma máquina).
                int hash = 0;
                foreach (char ch in name) hash = hash * 31 + ch;
                hash = Math.Abs(hash);
                _game.Settings.Archetype = hash % 3;
                _game.Settings.ColorIndex = (hash / 3) % ArtPalette.Heads.Length;
            }
            _game.Settings.PlayerKey = Guid.NewGuid().ToString("N");
            _game.Settings.ComfortMode = false;
            Application.runInBackground = true;
            Log("autopilot args: " + string.Join(" ", argv));
        }

        bool Has(string key) => _args.ContainsKey(key);
        string Arg(string key) => _args.TryGetValue(key, out string v) ? v : "";
        int IntArg(string key, int fallback) => int.TryParse(Arg(key), out int v) ? v : fallback;

        static void Log(string message) => Debug.Log("[AutoPilot] " + message);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            if (Has("-fsQuit") && _time > IntArg("-fsQuit", 600)) Quit("tempo limite");

            if (Has("-fsPortraits") && !_started) UpdatePortraits();
            else if (Has("-fsMenuShots") && !_started) UpdateMenuTour();
            float startAt = Has("-fsPortraits") ? 5f + Portraits.Length * 1.5f : Has("-fsMenuShots") ? 9.5f : 1f;
            if (!_started && _time > startAt)
            {
                _started = true;
                StartSession();
            }

            if (_pendingShot != null && _time >= _pendingShotAt)
            {
                Capture(_pendingShot);
                _pendingShot = null;
            }

            var net = NetGame.Instance;
            if (net == null || !net.HasSnapshot) return;
            var view = net.View;


            if (!view.InMatch)
            {
                UpdateLobby(net);
                return;
            }

            if (view.Phase == MatchPhase.MatchOver)
            {
                if (_overTime < 0f) _overTime = _time;
                if (!_reported)
                {
                    _reported = true;
                    WriteReport(net);
                    Schedule("results", 0.5f);
                }
                if (_time - _overTime > (net.IsLocalHost ? 8f : 4f)) Quit("partida terminada");
                return;
            }

            if (view.Phase == MatchPhase.PunishmentDraw) Schedule("punishment", 1.2f);
            if (view.Phase == MatchPhase.RiskReveal) Schedule("reveal" + view.RoundIndex, 1.6f);
            if (view.Phase == MatchPhase.RoundIntro) Schedule("round" + view.RoundIndex, 0.8f);
            // Eliminado: fotografa o ecrã "ESTÁS FORA", fecha-o e fotografa o modo espectador.
            var meNow = net.LocalPlayer;
            // Teste do modo espectador: o próprio jogador desiste na primeira tacada da ronda 1.
            // Sequência de capturas em terceira pessoa (modo espectador) para rever animações.
            if (Has("-fsBurst") && _game.Spectator != null && _game.Spectator.ControlsCamera && _burst < IntArg("-fsBurst", 30) && _time > _burstAt)
            {
                _burstAt = _time + 0.45f;
                if (Has("-fsBurstSide")) _game.Spectator.DebugOrbit(IntArg("-fsBurstSide", 80), 8f, 2.4f);
                _burst++;
                Capture("burst" + _burst.ToString("00"));
            }
            if (Has("-fsEliminateMe") && meNow != null && !meNow.Eliminated && view.Phase == MatchPhase.AwaitShot && _time > 20f && net.ServerDirector != null)
                net.ServerDirector.Forfeit(meNow.Id, "teste de espectador");
            if (meNow != null && meNow.Eliminated && view.Phase != MatchPhase.MatchOver)
            {
                Schedule("eliminated", 1.5f);
                if (_shots.Contains("eliminated") && _pendingShot == null && _elimSeenAt < 0f) _elimSeenAt = _time;
                if (_elimSeenAt >= 0f && _time > _elimSeenAt + 1.5f && !_shots.Contains("spectator_open"))
                {
                    _shots.Add("spectator_open");
                    _game.Hud.HandleBack();
                    Schedule("spectator", 2.5f, true);
                }
            }
            if (view.Phase == MatchPhase.Shop) Schedule("shop", 0.6f);
            if (view.Phase == MatchPhase.Simulating) Schedule("spectate", 0.4f);

            if (Has("-fsAutoplay")) UpdateAutoplay(net, view, dt);
        }

        static readonly (int shape, int color, int hat, int accessory)[] Portraits =
        {
            (0, 0, 0, 0), (1, 1, 0, 1), (2, 2, 0, 2), (0, 3, 2, 3), (1, 4, 2, 4), (2, 5, 2, 5), (0, 6, 3, 0), (1, 7, 3, 1), (2, 0, 3, 2), (0, 1, 4, 3), (1, 2, 4, 4), (2, 3, 4, 5), (0, 4, 5, 0), (1, 5, 5, 1), (2, 6, 5, 2),
        };

        /// <summary>-fsPortraits: mostra várias combinações na personagem de montra do menu e fotografa cada uma.</summary>
        void UpdatePortraits()
        {
            for (int i = 0; i < Portraits.Length; i++)
            {
                float at = 3f + i * 1.5f;
                if (_time < at || _shots.Contains("pset" + i)) continue;
                _shots.Add("pset" + i);
                var s = _game.Settings;
                (s.Archetype, s.ColorIndex, s.HatId, s.AccessoryId) = Portraits[i];
                _game.Hud.DebugShow("hidden");
                _pendingShot = null;
                Schedule("portrait" + (i + 1), 1.0f, true);
            }
        }

        /// <summary>Percorre os ecrãs do menu e tira screenshots (verificação visual dos concepts).</summary>
        void UpdateMenuTour()
        {
            string[] pages = { "main", "multiplayer", "create", "customize", "settings" };
            for (int i = 0; i < pages.Length; i++)
            {
                float at = 1.2f + i * 1.6f;
                if (_time >= at && !_shots.Contains("page_" + pages[i]))
                {
                    _shots.Add("page_" + pages[i]);
                    _game.Hud.DebugShow(pages[i]);
                    _pendingShot = null;
                    Schedule("menu_" + pages[i], 0.9f, true);
                }
            }
            if (_time >= 1.2f + pages.Length * 1.6f && !_shots.Contains("page_done"))
            {
                _shots.Add("page_done");
                _game.Hud.DebugShow("main");
            }
        }

        void StartSession()
        {
            if (Has("-fsPractice"))
            {
                _game.StartPractice(IntArg("-fsPractice", 3));
            }
            else if (Has("-fsHost"))
            {
                _game.HostLan((ushort)IntArg("-fsHost", 7790), "127.0.0.1", IntArg("-fsBots", 0));
            }
            else if (Has("-fsJoin"))
            {
                string[] parts = Arg("-fsJoin").Split(':');
                ushort port = parts.Length > 1 && ushort.TryParse(parts[1], out ushort p) ? p : SessionService.DefaultPort;
                _game.JoinLan(parts[0], port);
            }
            Log("sessão iniciada");
        }

        static readonly (Vector3 pos, float yaw, float pitch)[] TourPoses =
        {
            (new Vector3(0f, 0.05f, -3.2f), 0f, 10f),       // 1 mesa e roleta ao fundo
            (new Vector3(0f, 0.05f, 2.4f), 0f, -6f),        // 2 máquina da roleta de perto
            (new Vector3(-4.2f, 0.05f, 3.6f), -27f, 2f),    // 3 máquina de bolas
            (new Vector3(-4.8f, 0.05f, -1.2f), -108f, 6f),  // 4 máquina de pastilhas
            (new Vector3(-4.9f, 0.05f, 2.4f), -67f, 0f),    // 5 jukebox
            (new Vector3(4.3f, 0.05f, -0.6f), 107f, 0f),    // 6 máquina de sabotagem
            (new Vector3(-3.4f, 0.05f, -3.3f), -137f, 8f),  // 7 bar
            (new Vector3(2.8f, 0.05f, -3.0f), -147f, 6f),   // 8 lounge e TV
            (new Vector3(3.3f, 0.05f, -2.2f), 124f, 4f),    // 9 armazém
            (new Vector3(-2.4f, 0.05f, 1.6f), 124f, -12f),  // 10 candeeiros sobre a mesa
            (new Vector3(1.8f, 0.05f, 2.6f), -150f, 10f),   // 11 bots à volta da mesa
        };        int _tourIndex = -1;
        float _tourTimer;

        /// <summary>-fsTour: percorre pontos da arena na sala de espera e fotografa cada um.</summary>
        bool UpdateTour()
        {
            if (!Has("-fsTour") || NetPlayer.Local == null) return false;
            if (_tourIndex >= TourPoses.Length) return false;
            _tourTimer -= Time.unscaledDeltaTime;
            if (_tourTimer > 0f) return true;
            _tourIndex++;
            if (_tourIndex >= TourPoses.Length) return false;
            var pose = TourPoses[_tourIndex];
            NetPlayer.Local.TeleportTo(pose.pos, pose.yaw);
            NetPlayer.Local.SetLook(pose.yaw, pose.pitch);
            Schedule("tour" + (_tourIndex + 1), 1.2f, true);
            _tourTimer = 2f;
            return true;
        }

        bool _labLaunched;

        void UpdateLobby(NetGame net)
        {
            if (Has("-fsAnimLab"))
            {
                if (!_labLaunched && NetPlayer.Local != null && _time > 3f)
                {
                    _labLaunched = true;
                    AnimLab.Launch(Arg("-fsAnimLab"));
                }
                return;
            }
            Schedule("lobby", 3f);
            if (_time > 4f && UpdateTour()) return;
            var slot = net.LocalSlot;
            if (!net.IsLocalHost)
            {
                if (slot != null && !slot.Ready && Has("-fsAutoplay")) net.SetReady(true);
                return;
            }

            if (!_settingsSent && Has("-fsRounds"))
            {
                _settingsSent = true;
                var settings = net.View.Settings.Copy();
                settings.RoundLimit = IntArg("-fsRounds", settings.RoundLimit);
                if (Has("-fsRack")) settings.RackMode = (RackMode)IntArg("-fsRack", 0);
                if (Has("-fsBalls")) settings.BallCount = IntArg("-fsBalls", 0);
                settings.ShopSeconds = Has("-fsMachineShot") ? 14 : 8;
                net.HostApplySettings(settings);
            }

            int want = IntArg("-fsStartWhen", 0);
            if (want <= 0 || net.View.Slots.Count < want) return;
            _actionTimer += Time.unscaledDeltaTime;
            if (_actionTimer < 2f) return;
            _actionTimer = 0f;
            Log("a começar partida com " + net.View.Slots.Count + " lugares");
            net.HostStartMatch();
        }

        void UpdateAutoplay(NetGame net, MatchView view, float dt)
        {
            var me = net.LocalPlayer;
            if (me == null || me.Eliminated) return;
            UpdateAnytimeSabotage(net, view, me, dt);

            int key = view.Phase == MatchPhase.AwaitShot ? view.ShotSequence * 16 + view.CurrentPlayer :
                      view.Phase == MatchPhase.AwaitTrigger ? 100000 + view.RoundIndex :
                      view.Phase == MatchPhase.Shop ? 200000 + view.ShopWindow : -1;
            if (key < 0) return;
            if (key != _actedKey)
            {
                _actedKey = key;
                _actionTimer = 0f;
                _acted = false;
            }
            if (_acted) return;
            _actionTimer += dt;

            switch (view.Phase)
            {
                case MatchPhase.AwaitShot when view.CurrentPlayer == me.Id:
                    if (_actionTimer < 1.2f) return;
                    var plan = BotBrain.PlanShot(net.Mirror, view.BallInHand, view.KitchenOnly, _rng, 1f);
                    if (plan.WantsPlacement && !_placed)
                    {
                        _placed = true;
                        net.RequestPlaceCue(plan.Placement);
                        _actionTimer = 0.6f;
                        return;
                    }
                    if (!_aiming)
                    {
                        _aiming = true;
                        _game.Shot.DebugAim(plan.Direction, plan.Power);
                        if (_aimShots < 2) Schedule("aim" + (++_aimShots), 0.4f, true);
                        return;
                    }
                    if (_actionTimer < 2.2f) return;
                    net.RequestShot(plan.Direction, plan.Power, Vector2.zero);
                    _acted = true;
                    _aiming = false;
                    _placed = false;
                    break;
                case MatchPhase.AwaitTrigger when view.Selected == me.Id:
                    Schedule("trigger", 0.4f, true);
                    if (_actionTimer < 1.6f) return;
                    net.RequestTrigger();
                    _acted = true;
                    break;
                case MatchPhase.Shop:
                    if (Has("-fsMachineShot") && Has("-fsShots") && !_shots.Contains("machine") && NetPlayer.Local != null)
                    {
                        // Vai à máquina, abre-a (zoom) e fotografa a UI desenhada no ecrã.
                        if (_actionTimer < 0.3f) return;
                        var anchor = RoomBuilder.SabotageAnchor;
                        Vector3 front = anchor.rotation * Vector3.back;
                        NetPlayer.Local.TeleportTo(anchor.position + front * 1.3f, Quaternion.LookRotation(-front).eulerAngles.y);
                        _game.Interaction.DebugOpen(_game.SabotageMachine);
                        Schedule("machine", 1.4f, true);
                        _actionTimer = -1.2f;
                        return;
                    }
                    if (_actionTimer < 1f) return;
                    int tier = _rng.NextDouble() < 0.4 ? 1 : 0;
                    var targets = new List<int>();
                    foreach (var p in view.Players)
                        if (view.BlockFor(me.Id, p.Id, tier) == SabotageBlock.None) targets.Add(p.Id);
                    if (targets.Count > 0 && _rng.NextDouble() < 0.7) net.RequestPurchase(targets[_rng.Next(targets.Count)], tier);
                    else net.RequestPassShop();
                    _game.Interaction.Close();
                    _acted = true;
                    break;
            }
        }

        float _sabTimer;
        bool _machineOpened;

        /// <summary>Máquina sempre aberta: vai lá durante a vez de outro jogador, fotografa o ecrã e sabota.</summary>
        void UpdateAnytimeSabotage(NetGame net, MatchView view, PlayerView me, float dt)
        {
            bool can = view.SabotageAnytime && view.ShopOpen && me.SabotageCooldown <= 0f && me.Coins >= view.Settings.SabotageCost &&
                       view.Phase == MatchPhase.AwaitShot && view.CurrentPlayer != me.Id;
            if (!can)
            {
                if (_machineOpened) _game.Interaction.Close();
                _machineOpened = false;
                _sabTimer = Mathf.Min(_sabTimer, 0f);
                return;
            }
            _sabTimer += dt;
            if (Has("-fsMachineShot") && Has("-fsShots") && !_shots.Contains("machine") && !_machineOpened && _sabTimer > 0.3f && NetPlayer.Local != null)
            {
                var anchor = RoomBuilder.SabotageAnchor;
                Vector3 front = anchor.rotation * Vector3.back;
                NetPlayer.Local.TeleportTo(anchor.position + front * 1.3f, Quaternion.LookRotation(-front).eulerAngles.y);
                _game.Interaction.DebugOpen(_game.SabotageMachine);
                _machineOpened = true;
                Schedule("machine", 1.4f, true);
            }
            if (_sabTimer < 2.5f) return;
            int tier = _rng.NextDouble() < 0.4 ? 1 : 0;
            var targets = new List<int>();
            foreach (var p in view.Players)
                if (view.BlockFor(me.Id, p.Id, tier) == SabotageBlock.None) targets.Add(p.Id);
            if (targets.Count > 0) net.RequestPurchase(targets[_rng.Next(targets.Count)], tier);
            _game.Interaction.Close();
            _machineOpened = false;
            _sabTimer = -(float)(_rng.NextDouble() * 8.0);
        }

        float _elimSeenAt = -1f;
        int _burst;
        float _burstAt;
        bool _acted;
        bool _aiming;
        bool _placed;

        void Schedule(string name, float delay, bool force = false)
        {
            if (!Has("-fsShots") || _shots.Contains(name) || (_pendingShot != null && !force)) return;
            _shots.Add(name);
            _pendingShot = name;
            _pendingShotAt = _time + delay;
        }

        void Capture(string name)
        {
            string dir = Arg("-fsShots");
            Directory.CreateDirectory(dir);
            string path = Path.GetFullPath(Path.Combine(dir, name + ".png"));
            ScreenCapture.CaptureScreenshot(path);
            Log("screenshot " + path);
        }

        void WriteReport(NetGame net)
        {
            if (!Has("-fsReport")) return;
            var v = net.View;
            var sb = new StringBuilder();
            sb.Append("{\"winner\":").Append(v.MatchWinner);
            sb.Append(",\"rounds\":").Append(v.RoundIndex);
            sb.Append(",\"shots\":").Append(v.Telemetry.Shots);
            sb.Append(",\"discharges\":").Append(v.Telemetry.Discharges);
            sb.Append(",\"sabotages\":").Append(v.Telemetry.Sabotages);
            sb.Append(",\"ranking\":[").Append(string.Join(",", v.Ranking)).Append(']');
            sb.Append(",\"players\":[");
            for (int i = 0; i < v.Players.Count; i++)
            {
                var p = v.Players[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(p.Id).Append(",\"name\":\"").Append(p.Name.Replace("\"", "'"))
                  .Append("\",\"bot\":").Append(p.IsBot ? "true" : "false")
                  .Append(",\"wins\":").Append(p.RoundWins).Append(",\"lives\":").Append(p.Lives)
                  .Append(",\"coins\":").Append(p.Coins).Append(",\"pockets\":").Append(p.LegalPockets)
                  .Append(",\"eliminated\":").Append(p.Eliminated ? "true" : "false").Append('}');
            }
            sb.Append("],\"balls\":[");
            for (int i = 0; i < v.Balls.Count; i++)
            {
                var b = v.Balls[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(b.Number).Append(',').Append(b.Position.x.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                  .Append(b.Position.y.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',').Append(b.Pocketed ? 1 : 0).Append(']');
            }
            sb.Append("]}");
            File.WriteAllText(Arg("-fsReport"), sb.ToString());
            Log("relatório escrito em " + Arg("-fsReport"));
        }

        void Quit(string reason)
        {
            Log("a sair: " + reason);
            Application.Quit();
        }
    }
}
