using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    public enum NetEventType : byte
    {
        Feed,
        ShotResolved,
        RoundWon,
        Risk,
        Sabotage,
        Eliminated,
        MatchEnded,
        TurnStarted,
        Phase,
        Toast,
        Coins
    }

    /// <summary>
    /// Objecto de rede único da sessão. No servidor (listen server autoritativo — MULT-001) detém o lobby,
    /// o MatchDirector, os bots e a replicação; nos clientes mantém o MatchView recebido e o espelho da mesa.
    /// Os clientes só enviam intenções; o remetente de cada RPC é validado contra o lugar que ocupa.
    /// </summary>
    public sealed class NetGame : NetworkBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            Instance = null;
            PendingJoins.Clear();
            LocalJoin = default;
            InitialBots = 0;
            ServerJoinCode = "";
        }

        public const int ProtocolVersion = 4;
        public const float TurnDisconnectGrace = 20f;
        public const float ReconnectGrace = 90f;
        const float SnapshotInterval = 0.1f;
        const float FrameInterval = 1f / 30f;
        const float AimInterval = 1f / 15f;
        const ulong BotClientId = ulong.MaxValue;

        static readonly string[] BotNames = { "Bot Giz", "Bot Tabela", "Bot Caçapa", "Bot Efeito", "Bot Taco", "Bot Tacada" };

        /// <summary>Configuração e catálogo partilhados, injectados pelo GameBootstrap antes de ligar.</summary>
        public static MatchConfig SharedConfig;
        public static IReadOnlyList<ModifierDefinition> SharedCatalog;
        public static JoinPayload LocalJoin;
        public static SessionKind ServerKind;
        public static string ServerJoinCode = "";
        public static string ServerRoomName = "Sala";
        public static bool ServerIsPrivate;
        public static int InitialBots;
        /// <summary>Duração (s) de cada faixa da jukebox; fornecido pela apresentação (MusicLibrary).</summary>
        public static Func<int, float> TrackLength;
        public static int TrackCount;
        public static readonly Dictionary<ulong, JoinPayload> PendingJoins = new Dictionary<ulong, JoinPayload>();

        public static NetGame Instance { get; private set; }

        sealed class ServerSlot
        {
            public int SlotId;
            public ulong ClientId;
            public string Key;
            public PlayerProfile Profile;
            public bool Ready;
            public bool Connected;
            public float DisconnectedAt;
        }

        readonly List<ServerSlot> _slots = new List<ServerSlot>();
        readonly List<ServerSlot> _seatSlots = new List<ServerSlot>();
        readonly List<SeatInfo> _seats = new List<SeatInfo>();
        readonly List<SimContact> _pendingContacts = new List<SimContact>();
        readonly Dictionary<ulong, float> _lastEmote = new Dictionary<ulong, float>();
        readonly List<BallState> _frameBalls = new List<BallState>();
        readonly List<BallState> _sampled = new List<BallState>();
        readonly BallInterpolator _interpolator = new BallInterpolator();
        LobbySettings _settings = new LobbySettings();
        MatchDirector _director;
        BotBrain _bots;
        float _snapshotTimer;
        float _frameTimer;
        float _aimTimer;
        uint _frameSequence;
        uint _lastFrameSequence;
        bool _dirty;
        int _botNameIndex;
        int _purchaseNonce;

        public MatchView View { get; } = new MatchView();
        public bool HasSnapshot { get; private set; }
        public BilliardsSimulation Mirror { get; private set; }
        public AimState RemoteAim { get; private set; } = AimState.None;
        public MatchDirector ServerDirector => _director;
        public string LastLogPath { get; private set; }

        public event Action<string, FeedKind> Feed;
        /// <summary>Contacto na mesa: tipo, velocidade e posição (coordenadas da mesa) — para o som sair do sítio certo.</summary>
        public event Action<ContactType, float, Vector2> Contact;
        public event Action<int, FoulType, bool, bool> ShotResolved;
        public event Action<int> RoundWonEvent;
        public event Action<int, RiskOutcome, int, bool> RiskResolved;
        public event Action<int, int, int> SabotageApplied;
        public event Action<int> PlayerEliminated;
        public event Action MatchEnded;
        public event Action<int> TurnStarted;
        public event Action<MatchPhase> PhaseChanged;
        public event Action<string> Toast;
        public event Action<ulong, int> Emote;
        public event Action<int, int, string> CoinsChanged;
        public event Action<ulong, string> Chat;
        public event Action<ulong, Vector3, Vector3> Thrown;

        readonly Dictionary<ulong, float> _lastChat = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> _lastThrow = new Dictionary<ulong, float>();
        readonly HashSet<ulong> _skipVotes = new HashSet<ulong>();
        readonly System.Random _musicRng = new System.Random();
        int _playlistCursor = -1;

        public ulong LocalClientId => NetworkManager != null ? NetworkManager.LocalClientId : 0;
        public bool IsLocalHost => IsServer && IsClient;

        public PlayerView LocalPlayer => View.InMatch ? View.PlayerForClient(LocalClientId) : null;
        public SlotView LocalSlot => View.SlotForClient(LocalClientId);

        // =================================================================== lifecycle

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Mirror = new BilliardsSimulation(SharedConfig.Physics);

            if (!IsServer) return;
            _settings = LobbySettings.From(SharedConfig);
            _settings.Clamp();
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            foreach (var id in NetworkManager.ConnectedClientsIds) OnClientConnected(id);
            for (int i = 0; i < InitialBots; i++) AddBot();
            _dirty = true;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerUpdate(Time.deltaTime);
            else ClientUpdate(Time.deltaTime);
        }

        // =================================================================== server: lobby

        void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;
            foreach (var s in _slots)
                if (s.Connected && s.ClientId == clientId) return;

            JoinPayload payload;
            if (!PendingJoins.TryGetValue(clientId, out payload))
                payload = clientId == NetworkManager.LocalClientId ? LocalJoin : new JoinPayload { Key = "client-" + clientId, Profile = new PlayerProfile("Jogador", Archetype.Circle, Color.white) };
            PendingJoins.Remove(clientId);
            string key = string.IsNullOrEmpty(payload.Key) ? "client-" + clientId : payload.Key;

            foreach (var s in _slots)
            {
                if (s.Profile.IsBot || s.Connected || s.Key != key) continue;
                s.ClientId = clientId;
                s.Connected = true;
                BroadcastToast(s.Profile.Name + " voltou à sala.");
                _dirty = true;
                return;
            }

            foreach (var s in _slots)
                if (s.Connected && s.Key == key) key += "#" + clientId;

            if (_director != null)
            {
                BroadcastToast(payload.Profile.Name + " entrou como espectador.");
                return;
            }

            if (_slots.Count >= _settings.MaxPlayers && !RemoveOneBot())
            {
                BroadcastToast(payload.Profile.Name + " entrou como espectador (sala cheia).");
                return;
            }

            var profile = payload.Profile.Clone();
            profile.IsBot = false;
            profile.Color = UniqueColor(profile.Color, null);
            _slots.Add(new ServerSlot { SlotId = FreeSlotId(), ClientId = clientId, Key = key, Profile = profile, Connected = true });
            _slots.Sort((a, b) => a.SlotId.CompareTo(b.SlotId));
            BroadcastToast(profile.Name + " entrou na sala.");
            _dirty = true;
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;
            foreach (var s in _slots)
            {
                if (s.Profile.IsBot || !s.Connected || s.ClientId != clientId) continue;
                if (_director == null)
                {
                    _slots.Remove(s);
                    BroadcastToast(s.Profile.Name + " saiu da sala.");
                }
                else
                {
                    s.Connected = false;
                    s.DisconnectedAt = Time.time;
                    BroadcastToast(s.Profile.Name + " desligou-se — lugar reservado durante " + Mathf.RoundToInt(ReconnectGrace) + " s.");
                }
                _dirty = true;
                return;
            }
        }

        int FreeSlotId()
        {
            for (int id = 0; id < MatchView.MaxSeats; id++)
            {
                bool used = false;
                foreach (var s in _slots)
                    if (s.SlotId == id) used = true;
                if (!used) return id;
            }
            return _slots.Count;
        }

        bool AddBot()
        {
            if (_director != null || _slots.Count >= _settings.MaxPlayers) return false;
            var rng = new System.Random(Environment.TickCount + _botNameIndex * 7919);
            // Variedade: a forma menos usada na sala e uma cor da paleta que ninguém tenha.
            var shapeCount = new int[3];
            var palette = FriendSlop.Presentation.ArtPalette.Heads;
            var colorUsed = new bool[palette.Length];
            foreach (var s in _slots)
            {
                shapeCount[Mathf.Clamp((int)s.Profile.Archetype, 0, 2)]++;
                for (int c = 0; c < palette.Length; c++)
                    if (ColorsClose(palette[c], s.Profile.Color)) colorUsed[c] = true;
            }
            int shape = rng.Next(3);
            for (int k = 0; k < 3; k++)
                if (shapeCount[k] < shapeCount[shape]) shape = k;
            int colorIndex = rng.Next(palette.Length);
            for (int k = 0; k < palette.Length && colorUsed[colorIndex]; k++) colorIndex = (colorIndex + 1) % palette.Length;
            var profile = new PlayerProfile(BotNames[_botNameIndex++ % BotNames.Length], (Archetype)shape, palette[colorIndex])
            {
                IsBot = true,
                HatId = rng.Next(CosmeticIds.HatCount),
                AccessoryId = rng.Next(CosmeticIds.AccessoryCount)
            };
            profile.Wins = LoadBotWins(profile.Name);
            _slots.Add(new ServerSlot { SlotId = FreeSlotId(), ClientId = BotClientId, Key = "bot-" + _botNameIndex, Profile = profile, Connected = true, Ready = true });
            _slots.Sort((a, b) => a.SlotId.CompareTo(b.SlotId));
            _dirty = true;
            return true;
        }

        /// <summary>Duas pessoas com a mesma cor confundem-se na mesa: se a cor já está em uso, dá a próxima livre da paleta.</summary>
        Color UniqueColor(Color wanted, ServerSlot self)
        {
            var palette = FriendSlop.Presentation.ArtPalette.Heads;
            bool Taken(Color c)
            {
                foreach (var s in _slots)
                    if (s != self && ColorsClose(s.Profile.Color, c)) return true;
                return false;
            }
            if (!Taken(wanted)) return wanted;
            int start = 0;
            for (int i = 0; i < palette.Length; i++)
                if (ColorsClose(palette[i], wanted)) start = i;
            for (int k = 1; k <= palette.Length; k++)
            {
                var c = palette[(start + k) % palette.Length];
                if (!Taken(c)) return c;
            }
            return wanted;
        }

        static bool ColorsClose(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.08f;

        bool RemoveOneBot()
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                if (!_slots[i].Profile.IsBot) continue;
                _slots.RemoveAt(i);
                _dirty = true;
                return true;
            }
            return false;
        }

        bool IsHostSender(ulong sender) => sender == NetworkManager.LocalClientId || sender == NetworkManager.ServerClientId;

        ServerSlot SlotOf(ulong clientId)
        {
            foreach (var s in _slots)
                if (!s.Profile.IsBot && s.Connected && s.ClientId == clientId) return s;
            return null;
        }

        int PlayerOf(ulong clientId)
        {
            if (_director == null) return -1;
            for (int i = 0; i < _seatSlots.Count; i++)
                if (!_seatSlots[i].Profile.IsBot && _seatSlots[i].Connected && _seatSlots[i].ClientId == clientId) return i;
            return -1;
        }

        void ServerStartMatch()
        {
            if (_director != null && _director.Phase != MatchPhase.MatchOver) return;

            _slots.RemoveAll(s => !s.Connected && !s.Profile.IsBot);
            if (_slots.Count < SharedConfig.PlayerMin)
            {
                BroadcastToast("São precisos pelo menos " + SharedConfig.PlayerMin + " jogadores — adiciona bots ou espera por amigos.");
                return;
            }
            foreach (var s in _slots)
            {
                if (s.Profile.IsBot || s.Ready || IsHostSender(s.ClientId)) continue;
                BroadcastToast("À espera que " + s.Profile.Name + " fique pronto.");
                return;
            }

            var config = Instantiate(SharedConfig);
            _settings.ApplyTo(config);
            config.PlayerMax = Mathf.Max(_settings.MaxPlayers, _slots.Count);

            _seatSlots.Clear();
            _seatSlots.AddRange(_slots);
            var profiles = new List<PlayerProfile>();
            foreach (var s in _seatSlots) profiles.Add(s.Profile.Clone());

            int seed = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            _director = new MatchDirector(config, SharedCatalog, profiles, seed);
            _bots = new BotBrain(seed ^ 0x5bd1e995);
            HookDirector(_director);
            LastLogPath = null;
            foreach (var s in _slots) s.Ready = false;
            UpdateSeats();
            _director.Start();
            _dirty = true;
        }

        void ServerReturnToLobby()
        {
            if (_director != null && _director.Phase != MatchPhase.MatchOver) WriteMatchLog();
            _director = null;
            _bots = null;
            _slots.RemoveAll(s => !s.Connected && !s.Profile.IsBot);
            _dirty = true;
        }

        void HookDirector(MatchDirector d)
        {
            d.Feed += (text, kind) => EmitEvent(NetEventType.Feed, w => { w.Write(text); w.Write((byte)kind); });
            d.Contact += c =>
            {
                Contact?.Invoke(c.Type, c.Speed, c.Position);
                if (_pendingContacts.Count < 24) _pendingContacts.Add(c);
            };
            d.TurnStarted += id => EmitEvent(NetEventType.TurnStarted, w => w.Write((byte)id));
            d.ShotResolved += (record, outcome) => EmitEvent(NetEventType.ShotResolved, w =>
            {
                w.Write((byte)record.ShooterId);
                w.Write((byte)outcome.Foul);
                w.Write(outcome.KeepTurn);
                w.Write(outcome.RoundWon);
            });
            d.RoundWon += id => EmitEvent(NetEventType.RoundWon, w => w.Write((byte)id));
            d.RiskResolved += (player, outcome, modifier) => EmitEvent(NetEventType.Risk, w =>
            {
                w.Write((byte)player.Id);
                w.Write((byte)outcome);
                w.Write((short)(modifier != null ? MatchViewBuilder.IndexOf(SharedCatalog, modifier.Definition) : -1));
                w.Write(d.LastRiskEliminated);
            });
            d.SabotageApplied += r => EmitEvent(NetEventType.Sabotage, w =>
            {
                w.Write((byte)r.BuyerId);
                w.Write((byte)r.TargetId);
                w.Write((short)MatchViewBuilder.IndexOf(SharedCatalog, r.Applied.Definition));
            });
            d.PlayerEliminated += p => EmitEvent(NetEventType.Eliminated, w => w.Write((byte)p.Id));
            d.Economy.BalanceChanged += (p, delta, reason) => EmitEvent(NetEventType.Coins, w =>
            {
                w.Write((byte)p.Id);
                w.Write((short)delta);
                w.Write(reason ?? "");
            });
            d.PhaseChanged += phase =>
            {
                _dirty = true;
                EmitEvent(NetEventType.Phase, w => w.Write((byte)phase));
            };
            d.MatchEnded += () =>
            {
                RecordCareerWin(d);
                WriteMatchLog();
                EmitEvent(NetEventType.MatchEnded, w => { });
            };
        }

        const string BotWinsPrefix = "FriendSlop.BotWins.";

        static int LoadBotWins(string name)
        {
            try { return PlayerPrefs.GetInt(BotWinsPrefix + name, 0); }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Histórico de vitórias: o anfitrião soma a vitória ao perfil do vencedor (lugar e jogador, para todos verem já)
        /// e guarda a dos bots nas suas preferências. Cada humano guarda a sua localmente (GameBootstrap).
        /// </summary>
        void RecordCareerWin(MatchDirector d)
        {
            int winner = d.MatchWinnerId;
            if (winner < 0 || winner >= _seatSlots.Count) return;
            var slot = _seatSlots[winner];
            var player = d.GetPlayer(winner);
            int wins = Mathf.Max(slot.Profile.Wins, player != null ? player.Profile.Wins : 0) + 1;
            slot.Profile.Wins = wins;
            if (player != null) player.Profile.Wins = wins;
            if (slot.Profile.IsBot)
            {
                try
                {
                    PlayerPrefs.SetInt(BotWinsPrefix + slot.Profile.Name, wins);
                    PlayerPrefs.Save();
                }
                catch (Exception)
                {
                    // Preferências indisponíveis: o histórico do bot fica só nesta sessão.
                }
            }
            _dirty = true;
        }

        void UpdateSeats()
        {
            _seats.Clear();
            foreach (var s in _seatSlots)
                _seats.Add(new SeatInfo { SlotId = s.SlotId, ClientId = s.ClientId, Connected = s.Connected });
        }

        void WriteMatchLog()
        {
            if (_director == null) return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "MatchLogs");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, _director.MatchId + ".jsonl");
                File.WriteAllText(path, _director.Log.ToJsonLines());
                LastLogPath = path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendSlop] Não foi possível gravar o log da partida: " + e.Message);
            }
        }

        // =================================================================== server: loop

        /// <summary>Jukebox: avança para a próxima da fila (ou da playlist) quando a faixa acaba.</summary>
        void UpdateMusic()
        {
            if (TrackCount <= 0 || TrackLength == null) return;
            var music = View.Music;
            double now = NetworkManager.ServerTime.Time;
            bool finished = music.Track < 0 || now - music.StartedAt >= TrackLength(music.Track) - 0.05;
            if (finished) NextTrack(now);
        }

        void NextTrack(double now)
        {
            var music = View.Music;
            if (music.Queue.Count > 0)
            {
                music.Track = music.Queue[0];
                music.Queue.RemoveAt(0);
                if (music.QueuedBy.Count > 0) music.QueuedBy.RemoveAt(0);
            }
            else
            {
                _playlistCursor = _playlistCursor < 0 ? _musicRng.Next(TrackCount) : (_playlistCursor + 1) % TrackCount;
                music.Track = _playlistCursor;
            }
            music.StartedAt = now;
            _skipVotes.Clear();
            _dirty = true;
        }

        void ServerUpdate(float dt)
        {
            UpdateMusic();
            if (_director != null)
            {
                _director.Tick(dt);
                if (_director != null) _bots.Update(_director, dt);
                CheckDisconnects();
            }

            CaptureView();

            if (_director != null && (_director.Phase == MatchPhase.Simulating || _pendingContacts.Count > 0))
            {
                _frameTimer += dt;
                if (_frameTimer >= FrameInterval)
                {
                    _frameTimer = 0f;
                    SendFrame();
                }
            }

            if (_bots != null && _bots.Aim.IsValid)
            {
                RemoteAim = _bots.Aim;
                _aimTimer += dt;
                if (_aimTimer >= AimInterval)
                {
                    _aimTimer = 0f;
                    var a = _bots.Aim;
                    AimBroadcastRpc(a.PlayerId, a.Yaw, a.Pull, a.Spin, a.Placing, a.PlacePosition);
                }
            }

            _snapshotTimer += dt;
            if (_dirty || _snapshotTimer >= SnapshotInterval)
            {
                _snapshotTimer = 0f;
                _dirty = false;
                if (NetworkManager.ConnectedClientsIds.Count > 1) SnapshotRpc(MatchViewCodec.Serialize(View));
            }
        }

        void CheckDisconnects()
        {
            UpdateSeats();
            for (int i = 0; i < _seatSlots.Count && _director != null; i++)
            {
                var slot = _seatSlots[i];
                if (slot.Profile.IsBot || slot.Connected) continue;
                var player = _director.GetPlayer(i);
                if (player == null || player.Eliminated) continue;

                float gone = Time.time - slot.DisconnectedAt;
                if (_director.Phase == MatchPhase.AwaitShot && _director.CurrentPlayerId == i && gone > TurnDisconnectGrace)
                    _director.SkipTurn("desligado");
                if (gone > ReconnectGrace)
                    _director.Forfeit(i, "abandono");
            }
        }

        void CaptureView()
        {
            View.Kind = ServerKind;
            View.JoinCode = ServerJoinCode ?? "";
            View.RoomName = ServerRoomName ?? "Sala";
            View.IsPrivate = ServerIsPrivate;
            View.HostClientId = NetworkManager.LocalClientId;
            View.Music.SkipVotes = _skipVotes.Count;
            View.Music.SkipNeeded = Mathf.Max(1, Mathf.CeilToInt(NetworkManager.ConnectedClientsIds.Count * 0.5f));

            while (View.Slots.Count < _slots.Count) View.Slots.Add(new SlotView());
            while (View.Slots.Count > _slots.Count) View.Slots.RemoveAt(View.Slots.Count - 1);
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                var v = View.Slots[i];
                v.SlotId = s.SlotId;
                v.ClientId = s.ClientId;
                v.Ready = s.Ready || s.Profile.IsBot;
                v.Connected = s.Connected;
                v.Profile = s.Profile;
            }

            var settingsCopy = View.Settings;
            settingsCopy.RoundLimit = _settings.RoundLimit;
            settingsCopy.RiskGrowthPct = _settings.RiskGrowthPct;
            settingsCopy.RackMode = _settings.RackMode;
            settingsCopy.RiskDanger = _settings.RiskDanger;
            settingsCopy.RiskTotal = _settings.RiskTotal;
            settingsCopy.WinnerReward = _settings.WinnerReward;
            settingsCopy.ParticipationReward = _settings.ParticipationReward;
            settingsCopy.SabotageCost = _settings.SabotageCost;
            settingsCopy.CoinCap = _settings.CoinCap;
            settingsCopy.ShopSeconds = _settings.ShopSeconds;
            settingsCopy.TurnSeconds = _settings.TurnSeconds;
            settingsCopy.BallCount = _settings.BallCount;
            settingsCopy.MaxPlayers = _settings.MaxPlayers;
            settingsCopy.EnableSabotage = _settings.EnableSabotage;
            settingsCopy.EnableDebuffs = _settings.EnableDebuffs;
            settingsCopy.SabotageCooldown = _settings.SabotageCooldown;

            if (_director != null)
            {
                MatchViewBuilder.CaptureMatch(_director, SharedCatalog, _seats, View);
            }
            else
            {
                View.InMatch = false;
                View.Phase = MatchPhase.None;
                View.Players.Clear();
                View.Balls.Clear();
            }

            Mirror.LoadState(View.Balls, View.FinalBall);
            HasSnapshot = true;
        }

        void SendFrame()
        {
            _frameSequence++;
            using (var ms = new MemoryStream(512))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(_frameSequence);
                w.Write(Time.time);
                _director.Sim.CopyState(_frameBalls);
                MatchViewCodec.WriteBalls(w, _frameBalls);
                w.Write((byte)_pendingContacts.Count);
                foreach (var c in _pendingContacts)
                {
                    w.Write((byte)c.Type);
                    w.Write(c.Speed);
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(c.Position.x * 1000f), short.MinValue, short.MaxValue));
                    w.Write((short)Mathf.Clamp(Mathf.RoundToInt(c.Position.y * 1000f), short.MinValue, short.MaxValue));
                }
                _pendingContacts.Clear();
                w.Flush();
                if (NetworkManager.ConnectedClientsIds.Count > 1) FrameRpc(ms.ToArray());
            }
        }

        void EmitEvent(NetEventType type, Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream(64))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)type);
                write(w);
                w.Flush();
                EventRpc(ms.ToArray());
            }
        }

        void BroadcastToast(string text) => EmitEvent(NetEventType.Toast, w => w.Write(text));

        // =================================================================== client

        void ClientUpdate(float dt)
        {
            if (!HasSnapshot) return;
            if (View.PhaseDuration > 0f) View.PhaseRemaining = Mathf.Max(0f, View.PhaseRemaining - dt);

            if (View.Phase == MatchPhase.Simulating && _interpolator.HasFrames)
            {
                _interpolator.Sample(dt, _sampled);
                if (_sampled.Count > 0) Mirror.LoadState(_sampled, View.FinalBall);
            }
            else
            {
                Mirror.LoadState(View.Balls, View.FinalBall);
            }
        }

        [Rpc(SendTo.NotServer)]
        void SnapshotRpc(byte[] data)
        {
            MatchViewCodec.Deserialize(data, View);
            HasSnapshot = true;
            if (View.Phase != MatchPhase.Simulating) _interpolator.Reset();
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void FrameRpc(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                uint sequence = r.ReadUInt32();
                float time = r.ReadSingle();
                MatchViewCodec.ReadBalls(r, _frameBalls);
                int contacts = r.ReadByte();
                for (int i = 0; i < contacts; i++)
                {
                    var type = (ContactType)r.ReadByte();
                    float speed = r.ReadSingle();
                    var position = new Vector2(r.ReadInt16() / 1000f, r.ReadInt16() / 1000f);
                    Contact?.Invoke(type, speed, position);
                }

                // MULT-015: frames atrasados de estados anteriores são ignorados.
                if (sequence <= _lastFrameSequence && _lastFrameSequence - sequence < 1000000) return;
                _lastFrameSequence = sequence;
                _interpolator.Push(time, _frameBalls);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        void EventRpc(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                var type = (NetEventType)r.ReadByte();
                switch (type)
                {
                    case NetEventType.Feed:
                        string text = r.ReadString();
                        Feed?.Invoke(text, (FeedKind)r.ReadByte());
                        break;
                    case NetEventType.ShotResolved:
                        int shooter = r.ReadByte();
                        var foul = (FoulType)r.ReadByte();
                        bool keep = r.ReadBoolean();
                        ShotResolved?.Invoke(shooter, foul, keep, r.ReadBoolean());
                        break;
                    case NetEventType.RoundWon:
                        RoundWonEvent?.Invoke(r.ReadByte());
                        break;
                    case NetEventType.Risk:
                        int player = r.ReadByte();
                        var outcome = (RiskOutcome)r.ReadByte();
                        short riskDef = r.ReadInt16();
                        RiskResolved?.Invoke(player, outcome, riskDef, r.ReadBoolean());
                        break;
                    case NetEventType.Sabotage:
                        int buyer = r.ReadByte();
                        int target = r.ReadByte();
                        SabotageApplied?.Invoke(buyer, target, r.ReadInt16());
                        break;
                    case NetEventType.Eliminated:
                        PlayerEliminated?.Invoke(r.ReadByte());
                        break;
                    case NetEventType.MatchEnded:
                        MatchEnded?.Invoke();
                        break;
                    case NetEventType.TurnStarted:
                        TurnStarted?.Invoke(r.ReadByte());
                        break;
                    case NetEventType.Phase:
                        PhaseChanged?.Invoke((MatchPhase)r.ReadByte());
                        break;
                    case NetEventType.Toast:
                        Toast?.Invoke(r.ReadString());
                        break;
                    case NetEventType.Coins:
                        int who = r.ReadByte();
                        int delta = r.ReadInt16();
                        CoinsChanged?.Invoke(who, delta, r.ReadString());
                        break;
                }
            }
        }

        // =================================================================== commands (cliente → servidor)

        public void RequestShot(Vector2 direction, float power, Vector2 spin) => ShotRpc(direction, power, spin, View.ShotSequence);
        public void RequestPlaceCue(Vector2 position) => PlaceCueRpc(position);
        public void RequestTrigger() => TriggerRpc(new FixedString64Bytes(View.RiskEventId ?? ""));
        public void RequestPurchase(int targetId, int tier) => PurchaseRpc(targetId, tier, ++_purchaseNonce);
        public void QueueTrack(int track) => QueueTrackRpc(track);
        public void VoteSkipTrack() => SkipTrackRpc();
        public void HostKick(ulong clientId) => KickRpc(clientId);
        public void RequestPassShop() => PassShopRpc();
        public void SendAim(AimState aim) => AimRpc(aim.Yaw, aim.Pull, aim.Spin, aim.Placing, aim.PlacePosition);
        public void SendEmote(int emote) => EmoteRpc((byte)emote);
        public void SetReady(bool ready) => ReadyRpc(ready);
        public void SendProfile(PlayerProfile profile)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                MatchViewCodec.WriteProfile(w, profile);
                w.Flush();
                ProfileRpc(ms.ToArray());
            }
        }
        public void HostApplySettings(LobbySettings settings)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                settings.Write(w);
                w.Flush();
                SettingsRpc(ms.ToArray());
            }
        }
        public void HostSetBots(bool add) => BotRpc(add);
        public void HostStartMatch() => StartRpc();
        public void HostReturnToLobby() => LobbyRpc();

        [Rpc(SendTo.Server)]
        void ShotRpc(Vector2 direction, float power, Vector2 spin, int sequence, RpcParams rpcParams = default)
        {
            int player = PlayerOf(rpcParams.Receive.SenderClientId);
            if (player < 0) return;
            _director.SubmitShot(new ShotRequest { PlayerId = player, ShotSequence = sequence, Direction = direction, Power = power, SpinX = spin.x, SpinY = spin.y });
        }

        [Rpc(SendTo.Server)]
        void PlaceCueRpc(Vector2 position, RpcParams rpcParams = default)
        {
            int player = PlayerOf(rpcParams.Receive.SenderClientId);
            if (player >= 0) _director.RequestPlaceCueBall(player, position);
        }

        [Rpc(SendTo.Server)]
        void TriggerRpc(FixedString64Bytes riskEventId, RpcParams rpcParams = default)
        {
            int player = PlayerOf(rpcParams.Receive.SenderClientId);
            if (player >= 0) _director.PullTrigger(player, riskEventId.ToString());
        }

        [Rpc(SendTo.Server)]
        void PurchaseRpc(int targetId, int tier, int nonce, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            int player = PlayerOf(sender);
            if (player < 0) return;
            _director.SubmitPurchase(new PurchaseRequest
            {
                ClientRequestId = sender + "-" + _director.ShopWindowIndex + "-" + nonce,
                BuyerId = player,
                TargetId = targetId,
                Tier = tier
            });
        }

        [Rpc(SendTo.Server)]
        void QueueTrackRpc(int track, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            var music = View.Music;
            if (track < 0 || track >= TrackCount || music.Queue.Count >= 8) return;
            int mine = 0;
            foreach (ulong by in music.QueuedBy)
                if (by == sender) mine++;
            if (mine >= 2 && !IsHostSender(sender)) return;
            music.Queue.Add(track);
            music.QueuedBy.Add(sender);
            var slot = SlotOf(sender);
            BroadcastToast((slot != null ? slot.Profile.Name : "Alguém") + " pôs uma música na fila.");
            _dirty = true;
        }

        [Rpc(SendTo.Server)]
        void SkipTrackRpc(RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            _skipVotes.Add(sender);
            int needed = Mathf.Max(1, Mathf.CeilToInt(NetworkManager.ConnectedClientsIds.Count * 0.5f));
            if (IsHostSender(sender) || _skipVotes.Count >= needed)
            {
                NextTrack(NetworkManager.ServerTime.Time);
                BroadcastToast("Música saltada.");
            }
            _dirty = true;
        }

        [Rpc(SendTo.Server)]
        void KickRpc(ulong clientId, RpcParams rpcParams = default)
        {
            if (!IsHostSender(rpcParams.Receive.SenderClientId) || clientId == NetworkManager.LocalClientId) return;
            if (!NetworkManager.ConnectedClientsIds.Contains(clientId)) return;
            var slot = SlotOf(clientId);
            if (slot != null)
            {
                slot.Key = "kicked-" + Guid.NewGuid().ToString("N");
                BroadcastToast(slot.Profile.Name + " foi expulso pelo anfitrião.");
            }
            NetworkManager.DisconnectClient(clientId, "Foste expulso pelo anfitrião.");
        }

        [Rpc(SendTo.Server)]
        void PassShopRpc(RpcParams rpcParams = default)
        {
            int player = PlayerOf(rpcParams.Receive.SenderClientId);
            if (player >= 0) _director.PassShop(player);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void AimRpc(float yaw, float pull, Vector2 spin, bool placing, Vector2 place, RpcParams rpcParams = default)
        {
            int player = PlayerOf(rpcParams.Receive.SenderClientId);
            if (player < 0 || _director == null || player != _director.CurrentPlayerId) return;
            RemoteAim = new AimState { PlayerId = player, Yaw = yaw, Pull = Mathf.Clamp01(pull), Spin = spin, Placing = placing, PlacePosition = place };
            AimBroadcastRpc(player, yaw, pull, spin, placing, place);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        void AimBroadcastRpc(int player, float yaw, float pull, Vector2 spin, bool placing, Vector2 place)
        {
            RemoteAim = new AimState { PlayerId = player, Yaw = yaw, Pull = pull, Spin = spin, Placing = placing, PlacePosition = place };
        }

        [Rpc(SendTo.Server)]
        void EmoteRpc(byte emote, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_lastEmote.TryGetValue(sender, out float last) && Time.time - last < 0.8f) return;
            _lastEmote[sender] = Time.time;
            EmoteBroadcastRpc(sender, emote);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void EmoteBroadcastRpc(ulong clientId, byte emote)
        {
            Emote?.Invoke(clientId, emote);
        }

        public void SendChat(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 90) text = text.Substring(0, 90);
            ChatRpc(new FixedString128Bytes(text));
        }

        /// <summary>Objecto social atirado pelo jogador: física local em cada cliente, nunca toca nas bolas.</summary>
        public void SendThrow(Vector3 origin, Vector3 velocity) => ThrowRpc(origin, velocity);

        [Rpc(SendTo.Server)]
        void ChatRpc(FixedString128Bytes text, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_lastChat.TryGetValue(sender, out float last) && Time.time - last < 0.6f) return;
            _lastChat[sender] = Time.time;
            ChatBroadcastRpc(sender, text);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ChatBroadcastRpc(ulong sender, FixedString128Bytes text) => Chat?.Invoke(sender, text.ToString());

        [Rpc(SendTo.Server)]
        void ThrowRpc(Vector3 origin, Vector3 velocity, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_lastThrow.TryGetValue(sender, out float last) && Time.time - last < 0.9f) return;
            _lastThrow[sender] = Time.time;
            ThrowBroadcastRpc(sender, origin, Vector3.ClampMagnitude(velocity, 12f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ThrowBroadcastRpc(ulong sender, Vector3 origin, Vector3 velocity) => Thrown?.Invoke(sender, origin, velocity);

        [Rpc(SendTo.Server)]
        void ReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            var slot = SlotOf(rpcParams.Receive.SenderClientId);
            if (slot == null) return;
            slot.Ready = ready;
            _dirty = true;
        }

        [Rpc(SendTo.Server)]
        void ProfileRpc(byte[] data, RpcParams rpcParams = default)
        {
            var slot = SlotOf(rpcParams.Receive.SenderClientId);
            if (slot == null || _director != null) return;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms))
                {
                    var profile = new PlayerProfile("", Archetype.Circle, Color.white);
                    MatchViewCodec.ReadProfile(r, profile);
                    profile.IsBot = false;
                    profile.Name = JoinPayload.SanitizeName(profile.Name);
                    profile.HatId = Mathf.Clamp(profile.HatId, 0, CosmeticIds.HatCount - 1);
                    profile.AccessoryId = Mathf.Clamp(profile.AccessoryId, 0, CosmeticIds.AccessoryCount - 1);
                    profile.Color = UniqueColor(profile.Color, slot);
                    slot.Profile = profile;
                    _dirty = true;
                }
            }
            catch (EndOfStreamException)
            {
                // Pacote malformado: ignora.
            }
        }

        [Rpc(SendTo.Server)]
        void SettingsRpc(byte[] data, RpcParams rpcParams = default)
        {
            if (!IsHostSender(rpcParams.Receive.SenderClientId) || _director != null) return;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms))
                {
                    var settings = new LobbySettings();
                    settings.Read(r);
                    settings.Clamp();
                    _settings = settings;
                    _dirty = true;
                }
            }
            catch (EndOfStreamException)
            {
                // Pacote malformado: ignora.
            }
        }

        [Rpc(SendTo.Server)]
        void BotRpc(bool add, RpcParams rpcParams = default)
        {
            if (!IsHostSender(rpcParams.Receive.SenderClientId) || _director != null) return;
            if (add) AddBot();
            else RemoveOneBot();
        }

        [Rpc(SendTo.Server)]
        void StartRpc(RpcParams rpcParams = default)
        {
            if (IsHostSender(rpcParams.Receive.SenderClientId)) ServerStartMatch();
        }

        [Rpc(SendTo.Server)]
        void LobbyRpc(RpcParams rpcParams = default)
        {
            if (IsHostSender(rpcParams.Receive.SenderClientId)) ServerReturnToLobby();
        }
    }
}
