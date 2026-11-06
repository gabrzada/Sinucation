using System.Collections.Generic;
using System.Threading.Tasks;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    public enum AppScreen
    {
        Menu,
        Connecting,
        Session
    }

    /// <summary>Pedido de criação de sala vindo do menu multiplayer.</summary>
    public sealed class RoomRequest
    {
        public string Name = "Sala";
        public bool Online = true;
        public bool Private;
        public int MaxPlayers = 6;
        public int Rounds = 6;
        public int BallCount;
        public int Bots;
        public bool EnableSabotage = true;
        public bool EnableDebuffs = true;
        public ushort Port = SessionService.DefaultPort;
    }

    /// <summary>
    /// Ponto de entrada da cena: constrói arena, mesa, máquinas, câmara, HUD, áudio e liga o Netcode. Não contém
    /// regras: a autoridade é o NetGame no anfitrião; aqui só há apresentação, input local e gestão de sessão.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            Instance = null;
        }

        [SerializeField] MatchConfig _config;
        [SerializeField] ModifierDefinition[] _modifiers;
        [SerializeField] Material _litTemplate;
        [SerializeField] Material _unlitTemplate;
        [Tooltip("Material transparente de referência: garante a variante de shader dos fantasmas no build.")]
        [SerializeField] Material _litTransparentTemplate;
        [Tooltip("Material Complex Lit (clear coat) de referência: bolas e acabamentos envernizados.")]
        [SerializeField] Material _complexLitTemplate;
        [Tooltip("Material de partículas URP de referência: pó, faíscas e gosma.")]
        [SerializeField] Material _particleTemplate;
        [Tooltip("Materiais só para o build manter as variantes de shader (normal map, emissão, clear coat).")]
        [SerializeField] Material[] _shaderVariantKeepers;
        [SerializeField] NetworkManager _network;
        [SerializeField] GameObject _netGamePrefab;
        [SerializeField] GameObject _netPlayerPrefab;

        NetGame _boundNet;
        float _profileSendTimer = -1f;
        LobbySettings _pendingSettings;
        AvatarVisual _showcase;
        Transform _showcaseHolder;
        Task _arenaTask;
        bool _arenaReady;

        /// <summary>A arena acabou de montar (o modal de carregamento do menu fecha a seguir).</summary>
        public bool ArenaReady => _arenaReady;
        /// <summary>Progresso aproximado da montagem da arena (0–1), para a barra do modal.</summary>
        public float ArenaProgress { get; private set; }
        int _joinAttempt;
        bool _recovering;
        bool _quitting;
        string _directAddress;
        ushort _directPort;

        public static GameBootstrap Instance { get; private set; }

        public GameSettings Settings { get; private set; }
        public MatchConfig Config { get; private set; }
        public IReadOnlyList<ModifierDefinition> Catalog { get; private set; }
        public SessionService Session { get; private set; }
        public NetworkManager Network => _network;

        public RoomBuilder Room { get; private set; }
        public TableView Table { get; private set; }
        public FirstPersonRig Rig { get; private set; }
        public ShotController Shot { get; private set; }
        public CueView Cue { get; private set; }
        public RiskDeviceView Device { get; private set; }
        public ThrowablesView Throwables { get; private set; }
        public HudView Hud { get; private set; }
        public SfxService Sfx { get; private set; }
        public InteractionSystem Interaction { get; private set; }
        public SpectatorCamera Spectator { get; private set; }
        public GooFX Goo { get; private set; }
        public SabotageMachine SabotageMachine { get; private set; }
        public RandomizerMachine Randomizer { get; private set; }
        public JukeboxStation Jukebox { get; private set; }
        /// <summary>TV + leitor de DVD do lounge (mesma fila e estado da jukebox).</summary>
        public MediaCenter Media { get; private set; }
        public ArcadeHoop Hoop { get; private set; }
        public PingView Pings { get; private set; }
        public FunModeController Fun { get; private set; }

        public AppScreen Screen { get; private set; } = AppScreen.Menu;
        /// <summary>Menu de pausa aberto (Esc). Só congela a partida quando se joga sozinho contra bots — ver <see cref="MatchPaused"/>.</summary>
        public bool Paused { get; private set; }
        /// <summary>Partida congelada (um só humano contra bots): fases, bots, bolas e relógios parados até retomar.</summary>
        public bool MatchPaused => Screen == AppScreen.Session && NetGame.Instance != null && NetGame.Instance.MatchPaused;
        bool _timeFrozen;
        public bool CursorFree { get; private set; } = true;
        public string StatusMessage { get; private set; }

        /// <summary>Pose da câmara no menu principal (personagem de montra à direita, mesa ao fundo).</summary>
        public static readonly Vector3 MenuCameraPosition = new Vector3(-1.58f, 1.0f, -3.19f);
        public static readonly Vector3 MenuCameraTarget = new Vector3(-0.71f, 0.72f, -1.39f);

        public PlayerProfile LocalProfile => new PlayerProfile(JoinPayload.SanitizeName(Settings.PlayerName), (Archetype)Mathf.Clamp(Settings.Archetype, 0, 2),
            ArtPalette.Heads[Mathf.Clamp(Settings.ColorIndex, 0, ArtPalette.Heads.Length - 1)])
        {
            HatId = Settings.HatId,
            AccessoryId = Settings.AccessoryId,
            Wins = Settings.CareerWins
        };

        void Awake()
        {
            StartupMetrics.Mark("Awake");
            Instance = this;
            Application.wantsToQuit += AllowQuit;
            Settings = GameSettings.Load();
            GameplayCollision.Configure();
            Config = _config != null ? _config : ScriptableObject.CreateInstance<MatchConfig>();
            var catalog = HasValidModifiers() ? new List<ModifierDefinition>(_modifiers) : ModifierCatalog.CreateDefaults();
            // Append new definitions without changing indices already used by scene assets and snapshots.
            foreach (var def in ModifierCatalog.CreateDefaults())
            {
                if (catalog.Exists(d => d.Id == def.Id)) Destroy(def);
                else catalog.Add(def);
            }
            Catalog = catalog;
            VisualFactory.Initialize(_litTemplate, _unlitTemplate);
            VisualFactory.InitializeComplex(_complexLitTemplate);
            VisualFactory.InitializeParticles(_particleTemplate);
            // Só existem para o build manter variantes de shader; referenciá-los evita avisos de campo sem uso.
            if (_litTransparentTemplate == null || _shaderVariantKeepers == null) Debug.Log("[FriendSlop] Materiais de referência em falta: corre FriendSlop ▸ Criar ou reconstruir cena de jogo.");
            NetGame.SharedConfig = Config;
            NetGame.SharedCatalog = Catalog;

            foreach (var light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional) light.enabled = false;

            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            GraphicsQuality.Initialize(cam, Settings.Graphics);

            Rig = cam.gameObject.AddComponent<FirstPersonRig>();
            Rig.Initialize(this, cam);
            Interaction = CreateChild<InteractionSystem>("Interaction");
            Interaction.Initialize(this);
            Spectator = CreateChild<SpectatorCamera>("Spectator");
            Spectator.Initialize(this);
            Shot = CreateChild<ShotController>("Shot");
            Shot.Initialize(this);
            Sfx = CreateChild<SfxService>("Audio");
            Sfx.Initialize(this, true);
            StartupMetrics.Mark("Audio");
            Hud = CreateChild<HudView>("Hud");
            Hud.Initialize(this);
            Hud.ShowLoading("A PREPARAR A ARENA", () => _arenaReady, 0.6f, () => ArenaProgress, "A montar a sala, a mesa e o público");
            BuildShowcase();

            GraphicsQuality.Apply(cam);
            // Testes automáticos usam -screen-* (janelas lado a lado): não impor o modo de ecrã guardado.
            if (!AutoPilot.HasArguments()) GraphicsQuality.ApplyDisplay();
            SetupNetwork();
            if (AutoPilot.HasArguments()) gameObject.AddComponent<AutoPilot>();
            StartupMetrics.Mark("Menu ready");
            // A arena é o fundo do menu (a câmara da montra aponta para a mesa): começa já, sem esperar por Jogar.
            _arenaTask = BuildArena();
            _arenaTask.ContinueWith(t => Debug.LogException(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        }

        async Task BuildArena()
        {
            if (Screen == AppScreen.Connecting) StatusMessage = "A preparar a arena…";
            // Let the connecting screen render before the expensive asset upload.
            await Task.Yield();
            await Task.Yield();
            Room = CreateChild<RoomBuilder>("Room");
            var roomStages = Room.BuildStages(Config.Physics.TableLength*.5f, Config.Physics.TableWidth*.5f);
            while (roomStages.MoveNext())
            {
                ArenaProgress = Mathf.Min(0.42f, ArenaProgress + 0.06f);
                await Task.Yield();
                if (this == null) return;
            }
            StartupMetrics.Mark("Arena: sala");
            ArenaProgress = 0.45f;
            GraphicsQuality.ShadowLights = Room.ShadowLights;
            GraphicsQuality.Dust = Room.Dust;
            GraphicsQuality.Probe = Room.Probe;
            Table = CreateChild<TableView>("Table");
            Table.Initialize(Config.Physics);
            await Task.Yield();
            StartupMetrics.Mark("Arena: mesa");
            ArenaProgress = 0.55f;
            Goo = CreateChild<GooFX>("Goo"); Goo.Initialize();
            Randomizer = CreateAt<RandomizerMachine>("RandomizerMachine", RoomBuilder.RandomizerAnchor);
            Randomizer.Build(this, Catalog);
            await Task.Yield();
            if (this == null) return;
            StartupMetrics.Mark("Arena: roleta");
            ArenaProgress = 0.62f;
            SabotageMachine = CreateAt<SabotageMachine>("SabotageMachine", RoomBuilder.SabotageAnchor);
            SabotageMachine.Build();
            await Task.Yield();
            if (this == null) return;
            StartupMetrics.Mark("Arena: sabotagem");
            ArenaProgress = 0.7f;
            Jukebox = CreateAt<JukeboxStation>("Jukebox", RoomBuilder.JukeboxAnchor);
            Jukebox.Build(this);
            Media = CreateAt<MediaCenter>("MediaCenter", RoomBuilder.MediaAnchor);
            Media.Build(this);
            Hoop = CreateAt<ArcadeHoop>("ArcadeHoop", RoomBuilder.HoopAnchor);
            Hoop.Build(this);
            var crowdStages = CreateChild<SpectatorCrowd>("Crowd").BuildStages(Room.BalconySpots, GraphicsQuality.CrowdCount);
            while (crowdStages.MoveNext())
            {
                ArenaProgress = Mathf.Min(0.93f, ArenaProgress + 0.015f);
                await Task.Yield();
                if (this == null) return;
            }
            StartupMetrics.Mark("Arena: público");
            ArenaProgress = 0.95f;
            await Task.Yield();
            Cue = CreateChild<CueView>("Cue"); Cue.Initialize(this);
            Device = CreateChild<RiskDeviceView>("RiskDevice");
            var rest = Randomizer.RestPose;
            Device.Initialize(this, rest.position, rest.rotation);
            CreateChild<NpcAvatars>("Bots");
            Throwables = CreateChild<ThrowablesView>("Throwables"); Throwables.Initialize(this);
            Pings = CreateChild<PingView>("Pings"); Pings.Initialize(this);
            Fun = CreateChild<FunModeController>("FunMode"); Fun.Initialize(this);
            MusicLibrary.Register();
            Sfx.Initialize(this);
            GraphicsQuality.Apply(Rig.Camera);
            _arenaReady = true;
            ArenaProgress = 1f;
            StartupMetrics.Mark("Arena ready");
        }

        bool HasValidModifiers()
        {
            if (_modifiers == null || _modifiers.Length == 0) return false;
            foreach (var m in _modifiers)
                if (m == null) return false;
            return true;
        }

        T CreateChild<T>(string childName) where T : Component
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        T CreateAt<T>(string childName, Pose pose) where T : Component
        {
            var c = CreateChild<T>(childName);
            c.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return c;
        }

        // ------------------------------------------------------------------ montra do menu

        void BuildShowcase()
        {
            _showcaseHolder = new GameObject("MenuShowcase").transform;
            _showcaseHolder.SetParent(transform, false);
            _showcaseHolder.position = new Vector3(-0.2f, 0f, -1.75f);
            Vector3 look = MenuCameraPosition - _showcaseHolder.position;
            look.y = 0f;
            _showcaseHolder.rotation = Quaternion.LookRotation(look.normalized) * Quaternion.Euler(0f, -18f, 0f);
            _showcase = AvatarVisual.Create(_showcaseHolder, LocalProfile);
        }

        void UpdateShowcase()
        {
            bool visible = Screen != AppScreen.Session;
            if (_showcaseHolder.gameObject.activeSelf != visible) _showcaseHolder.gameObject.SetActive(visible);
            if (!visible) return;
            _showcase.Apply(LocalProfile);
            var pose = Hud != null && Hud.ShowcaseCelebrate ? AvatarPose.Celebrate : AvatarPose.Idle;
            _showcase.Animate(Time.deltaTime, pose, 6f, false);
        }

        // ------------------------------------------------------------------ rede

        void SetupNetwork()
        {
            if (_network == null) _network = FindAnyObjectByType<NetworkManager>();
            if (_network == null)
            {
                var go = new GameObject("NetworkManager");
                _network = go.AddComponent<NetworkManager>();
            }
            if (_network.NetworkConfig.NetworkTransport == null)
            {
                var transport = _network.GetComponent<UnityTransport>();
                if (transport == null) transport = _network.gameObject.AddComponent<UnityTransport>();
                _network.NetworkConfig.NetworkTransport = transport;
            }
            // Margem para snapshots com 6 jogadores + fila da jukebox (o padrão de 6 KB por mensagem é justo).
            if (_network.NetworkConfig.NetworkTransport is UnityTransport utp && utp.MaxPayloadSize < 16 * 1024)
            {
                utp.MaxPayloadSize = 16 * 1024;
                utp.MaxSendQueueSize = Mathf.Max(utp.MaxSendQueueSize, 16 * utp.MaxPayloadSize);
            }

            if (_netPlayerPrefab != null)
            {
                _network.NetworkConfig.PlayerPrefab = _netPlayerPrefab;
                if (!IsRegistered(_netPlayerPrefab)) _network.AddNetworkPrefab(_netPlayerPrefab);
            }
            else
            {
                Debug.LogError("[FriendSlop] Falta o prefab NetPlayer. Usa o menu FriendSlop ▸ Criar ou reconstruir cena de jogo.");
            }
            if (_netGamePrefab != null && !IsRegistered(_netGamePrefab)) _network.AddNetworkPrefab(_netGamePrefab);

            // Suavidade: +1 tick (33 ms) de margem no buffer de interpolação absorve oscilações de rede sem paragens;
            // o relógio de rede ressincroniza periodicamente (sessões longas não derivam).
            Unity.Netcode.Components.NetworkTransform.InterpolationBufferTickOffset = 1;
            _network.NetworkConfig.EnableTimeResync = true;
            // 60 ticks/s: medido anfitrião+cliente, os bonecos remotos param metade das vezes que a 30 (4% vs 8% dos frames)
            // e o p99,9 do frame desce de ~22 para 15 ms; custo de rede desprezável (só os transforms dos jogadores).
            _network.NetworkConfig.TickRate = 60;
            // QA: -fsTick N para comparar taxas de tick (todos os processos da sessão têm de usar o mesmo valor).
            var cmd = System.Environment.GetCommandLineArgs();
            int ti = System.Array.IndexOf(cmd, "-fsTick");
            if (ti >= 0 && ti + 1 < cmd.Length && uint.TryParse(cmd[ti + 1], out uint tick) && tick >= 10 && tick <= 120) _network.NetworkConfig.TickRate = tick;
            _network.NetworkConfig.ConnectionApproval = true;
            _network.ConnectionApprovalCallback = Approve;
            _network.OnServerStarted += OnServerStarted;
            _network.OnClientStopped += OnClientStopped;
            Session = new SessionService(_network);
        }

        /// <summary>O NGO pode já ter o prefab na lista automática (DefaultNetworkPrefabs); não duplicar.</summary>
        bool IsRegistered(GameObject prefab)
        {
            var prefabs = _network.NetworkConfig.Prefabs;
            if (prefabs.Contains(prefab)) return true;
            if (prefabs.NetworkPrefabsLists == null) return false;
            foreach (var list in prefabs.NetworkPrefabsLists)
            {
                if (list == null) continue;
                foreach (var entry in list.PrefabList)
                    if (entry.Prefab == prefab) return true;
            }
            return false;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (!JoinPayload.TryDecode(request.Payload, out var payload) || payload.Version != NetGame.ProtocolVersion)
            {
                response.Approved = false;
                response.Reason = "Versão do jogo diferente da do anfitrião.";
                return;
            }
            if (_network.ConnectedClientsIds.Count >= SessionService.MaxConnections)
            {
                response.Approved = false;
                response.Reason = "Sala cheia.";
                return;
            }
            // Partida em pausa (anfitrião sozinho contra bots): ninguém entra até retomar.
            string refusal = request.ClientNetworkId != _network.LocalClientId && NetGame.Instance != null ? NetGame.Instance.JoinRefusal(payload.Profile?.Name) : null;
            if (refusal != null)
            {
                response.Approved = false;
                response.Reason = refusal;
                return;
            }
            NetGame.PendingJoins[request.ClientNetworkId] = payload;
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Pending = false;
        }

        void OnServerStarted()
        {
            if (_netGamePrefab == null) return;
            var instance = Instantiate(_netGamePrefab);
            instance.GetComponent<NetworkObject>().Spawn();
        }

        void OnClientStopped(bool wasHost)
        {
            if (_quitting || _recovering || Session != null && (Session.Leaving || Session.Busy)) return;
            string reason = _network != null ? _network.DisconnectReason : null;
            if (!wasHost && Screen == AppScreen.Session &&
                (!string.IsNullOrEmpty(_directAddress) || !string.IsNullOrEmpty(Session.RecoverableOnlineId)) &&
                _network.DisconnectEvent != NetworkTransport.DisconnectEvents.AuthenticationFailure && SessionService.HasTransientDisconnectReason(reason))
            {
                RecoverConnection();
                return;
            }
            if (Screen != AppScreen.Menu)
                StatusMessage = string.IsNullOrEmpty(reason) ? (wasHost ? "Sessão terminada." : "Ligação ao anfitrião terminada.") : reason;
            Screen = AppScreen.Menu;
            Paused = false;
            _boundNet = null;
            _pendingSettings = null;
            Interaction.Close();
            NetGame.PendingJoins.Clear();
        }

        async Task<bool> PrepareLocalJoin()
        {
            int attempt = ++_joinAttempt;
            Screen = AppScreen.Connecting;
            try
            {
                if (!_arenaReady) await (_arenaTask ??= BuildArena());
                if (attempt != _joinAttempt) return false;
                await Session.Leave();
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
                Fail("Não foi possível preparar a ligação: " + error.Message);
                return false;
            }
            if (this == null || attempt != _joinAttempt) return false;
            _directAddress = null;
            Settings.Save();
            NetGame.LocalJoin = new JoinPayload { Version = NetGame.ProtocolVersion, Key = Settings.PlayerKey, Profile = LocalProfile };
            _network.NetworkConfig.ConnectionData = NetGame.LocalJoin.Encode();
            StatusMessage = null;
            Paused = false;
            return true;
        }

        public async void StartPractice(int bots)
        {
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            StatusMessage = "A preparar o treino…";
            _pendingSettings = LobbySettings.From(Config);
            _pendingSettings.BotDifficulty = Settings.BotDifficulty;
            _pendingSettings.Clamp();
            if (!Session.StartOffline(bots)) Fail(Session.LastError);
        }

        /// <summary>Compatibilidade (AutoPilot/testes): sala LAN pública sem regras extra.</summary>
        public async void HostLan(ushort port, string listenAddress, int bots)
        {
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            if (!Session.HostDirect(port, listenAddress, bots, "Sala de " + LocalProfile.Name)) Fail(Session.LastError);
        }

        /// <summary>Cria sala (online por código, ou LAN) com as regras escolhidas; aplicadas assim que a sala abre.</summary>
        public async void HostRoom(RoomRequest request)
        {
            Settings.LastRoomName = request.Name;
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            _pendingSettings = LobbySettings.From(Config);
            _pendingSettings.MaxPlayers = request.MaxPlayers;
            _pendingSettings.RoundLimit = request.Rounds;
            _pendingSettings.BallCount = request.BallCount;
            _pendingSettings.EnableSabotage = request.EnableSabotage;
            _pendingSettings.EnableDebuffs = request.EnableDebuffs;
            _pendingSettings.BotDifficulty = Settings.BotDifficulty;
            _pendingSettings.Clamp();

            if (request.Online)
            {
                StatusMessage = "A criar sala online…";
                if (!await Session.HostOnline(request.Name, request.Private, request.MaxPlayers)) Fail(Session.LastError);
            }
            else
            {
                StatusMessage = "A abrir sala na rede local…";
                if (!Session.HostDirect(request.Port, "0.0.0.0", request.Bots, request.Name, request.Private)) Fail(Session.LastError);
            }
        }

        public async void JoinLan(string address, ushort port)
        {
            Settings.LastAddress = address;
            if (!await PrepareLocalJoin()) return;
            _directAddress = address;
            _directPort = port;
            Screen = AppScreen.Connecting;
            StatusMessage = "A ligar a " + address + ":" + port + "…";
            if (!Session.JoinDirect(address, port)) Fail(Session.LastError);
        }

        async void RecoverConnection()
        {
            _recovering = true;
            string onlineId = Session.RecoverableOnlineId;
            int attempt = ++_joinAttempt;
            Screen = AppScreen.Connecting;
            Paused = false;
            _boundNet = null;
            Interaction.Close();
            try
            {
                for (int retry = 0; retry < 3 && attempt == _joinAttempt; retry++)
                {
                    StatusMessage = "A recuperar a ligação… " + (retry + 1) + "/3";
                    Debug.Log("[Session] Reconnect attempt " + (retry + 1));
                    await Session.Leave();
                    await Task.Delay(1000);
                    if (this == null || attempt != _joinAttempt) return;
                    bool started = string.IsNullOrEmpty(onlineId)
                        ? Session.JoinDirect(_directAddress, _directPort)
                        : await Session.JoinOnlineById(onlineId);
                    if (!started) continue;
                    float deadline = Time.realtimeSinceStartup + 10f;
                    while (Time.realtimeSinceStartup < deadline && attempt == _joinAttempt)
                    {
                        if (_network.IsConnectedClient && NetGame.Instance != null && NetGame.Instance.HasSnapshot && NetPlayer.Local != null)
                        {
                            Screen = AppScreen.Session;
                            StatusMessage = null;
                            Hud.ShowToast("Ligação recuperada.");
                            Debug.Log("[Session] Reconnected player=" + NetGame.Instance.LocalPlayer?.Id);
                            return;
                        }
                        await Task.Yield();
                    }
                }
                if (attempt == _joinAttempt) Fail("Não foi possível recuperar a ligação ao anfitrião.");
            }
            catch (System.Exception error)
            {
                if (attempt == _joinAttempt) Fail("Falha na recuperação: " + error.Message);
            }
            finally { _recovering = false; }
        }

        public async void JoinOnline(string code)
        {
            Settings.LastCode = code;
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            StatusMessage = "A entrar na sala " + code + "…";
            if (!await Session.JoinOnline(code)) Fail(Session.LastError);
        }

        public async void JoinOnlineById(string sessionId, string roomName)
        {
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            StatusMessage = "A entrar em \"" + roomName + "\"…";
            if (!await Session.JoinOnlineById(sessionId)) Fail(Session.LastError);
        }

        public void JoinListing(RoomListing room)
        {
            if (room.Online) JoinOnlineById(room.SessionId, room.Name);
            else JoinLan(room.Address, room.Port);
        }

        /// <summary>Partida rápida: primeira sala pública com lugar (online, depois LAN); senão cria uma pública.</summary>
        public async void QuickMatch()
        {
            if (!await PrepareLocalJoin()) return;
            Screen = AppScreen.Connecting;
            StatusMessage = "À procura de uma sala aberta…";
            await Session.RefreshOnlineRooms();
            foreach (var room in Session.OnlineRooms)
            {
                if (room.InMatch || room.Players >= room.MaxPlayers) continue;
                JoinOnlineById(room.SessionId, room.Name);
                return;
            }
            foreach (var room in Session.Lan.Rooms)
            {
                if (room.InMatch || room.Players >= room.MaxPlayers) continue;
                JoinLan(room.Address, room.Port);
                return;
            }
            Screen = AppScreen.Menu;
            HostRoom(new RoomRequest { Name = "Sala de " + LocalProfile.Name, Online = true, Private = false });
        }

        async void Fail(string message)
        {
            Screen = AppScreen.Menu;
            StatusMessage = message;
            _pendingSettings = null;
            try { await Session.Leave(); }
            catch (System.Exception error) { Debug.LogWarning("[FriendSlop] " + error.Message); }
        }

        public async void Leave()
        {
            ++_joinAttempt;
            Paused = false;
            Screen = AppScreen.Menu;
            _boundNet = null;
            _pendingSettings = null;
            Interaction.Close();
            await Session.Leave();
        }

        public void Quit()
        {
            _quitting = true;
            ++_joinAttempt;
            Settings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void TogglePause() => SetPauseMenu(!Paused, true);

        /// <summary>
        /// Abre/fecha o menu de pausa. Sozinho contra bots, abri-lo congela a partida e fechá-lo (CONTINUAR/Esc) retoma-a;
        /// os atalhos que fecham o menu para abrir outro painel (Personalizar, Painel do anfitrião) deixam-na parada.
        /// </summary>
        public void SetPauseMenu(bool open, bool resumeMatch)
        {
            if (open == Paused) return;
            Paused = open;
            var net = NetGame.Instance;
            if (open && Screen == AppScreen.Session && net != null && net.CanPauseMatch && !net.MatchPaused) net.RequestMatchPause(true);
            else if (!open && resumeMatch && net != null && net.MatchPaused) net.RequestMatchPause(false);
            if (!Paused) Settings.Save();
        }

        /// <summary>Tecla P / botão do aviso: pausa ou retoma a partida (só quando se joga sozinho contra bots).</summary>
        public void ToggleMatchPause()
        {
            var net = NetGame.Instance;
            if (Screen != AppScreen.Session || net == null) return;
            if (net.MatchPaused) net.RequestMatchPause(false);
            else if (net.CanPauseMatch) net.RequestMatchPause(true);
        }

        /// <summary>Guarda o perfil e, em sessão, envia-o ao anfitrião (com pequeno atraso para o nome).</summary>
        public void ProfileChanged(bool immediate)
        {
            _profileSendTimer = immediate ? 0f : 0.8f;
        }

        public void ApplyGraphics()
        {
            GraphicsQuality.Apply(Rig.Camera);
            GraphicsQuality.ApplyDisplay();
            Settings.Save();
        }

        // ------------------------------------------------------------------ loop

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _boundNet && net.IsSpawned)
            {
                _boundNet = net;
                net.BotReadyToShoot = id => NpcAvatars.Instance == null || NpcAvatars.Instance.ReadyToShoot(net, id);
                Hud.Bind(net);
                Sfx.Bind(net);
                Device.Bind(net);
                Throwables.Bind(net);
                Hoop.Bind(net);
                SabotageMachine.Bind(net);
                Randomizer.Bind(net);
            }

            if (Screen == AppScreen.Connecting && net != null && net.HasSnapshot && NetPlayer.Local != null)
            {
                Screen = AppScreen.Session;
                // Os avatares da sala (bots e jogadores) nascem agora; com a GI por ray tracing ligada, o Surface Cache
                // congela 2–3 s uns frames depois a integrar as malhas novas. O modal fica por cima até isso passar
                // (depois do tempo mínimo só fecha com 10 frames estáveis seguidos).
                float enteredAt = Time.unscaledTime;
                float settle = GraphicsQuality.Options.GlobalIllumination && GraphicsQuality.GlobalIlluminationSupported ? 1.6f : 0.4f;
                Hud.ShowLoading("A ENTRAR NA SALA", () => Time.unscaledTime - enteredAt >= settle, 0.5f, null, "A preparar os jogadores e a iluminação");
            }
            // OnClientStopped owns disconnect transitions. IsListening becomes false before that callback;
            // changing screens here would discard the active-session context needed for recovery.

            if (_pendingSettings != null && net != null && net.IsSpawned && net.IsLocalHost && net.HasSnapshot)
            {
                net.HostApplySettings(_pendingSettings);
                _pendingSettings = null;
            }
            if (net != null && net.IsSpawned) Session.HostTick(net);
            if (Screen == AppScreen.Menu) Session.Lan.Poll();

            if (_profileSendTimer >= 0f)
            {
                _profileSendTimer -= Time.unscaledDeltaTime;
                if (_profileSendTimer < 0f)
                {
                    Settings.Save();
                    if (net != null && net.IsSpawned) net.SendProfile(LocalProfile);
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (Hud.ChatOpen) Hud.CloseChat();
                    else if (Interaction.Active != null) Interaction.Close();
                    else if (Hud.HandleBack()) { }
                    else if (Screen == AppScreen.Session || Paused) TogglePause();
                }
                if (keyboard.f1Key.wasPressedThisFrame)
                {
                    Settings.ShowHelp = !Settings.ShowHelp;
                    Settings.Save();
                }
                if (Screen == AppScreen.Session && keyboard.pKey.wasPressedThisFrame && !Paused && !Hud.ChatOpen && !Hud.TextInputFocused && GUIUtility.keyboardControl == 0)
                    ToggleMatchPause();
                if (Screen == AppScreen.Session && keyboard.tabKey.wasPressedThisFrame && !Shot.UsesShotCamera && Interaction.Active == null && !Hud.ChatOpen && !Paused)
                {
                    // No fim da partida o Tab alterna entre o placar e o modo de brincadeira; no resto, solta o rato.
                    if (!Hud.HandleTab()) Hud.PanelCursor = !Hud.PanelCursor;
                }
            }

            UpdateShowcase();

            // Pausa contra bots: no anfitrião (o único humano) tudo o que é local também pára — animações, ragdolls,
            // objectos atirados. A rede e a música usam tempo real e continuam.
            bool freeze = MatchPaused && net.IsServer;
            if (freeze != _timeFrozen)
            {
                _timeFrozen = freeze;
                Time.timeScale = freeze ? 0f : 1f;
            }

            bool free = Screen != AppScreen.Session || Paused || MatchPaused || Hud.WantsCursor || Interaction.Active != null;
            if (AutoPilot.Active) free = false;
            CursorFree = free;
            if (!AutoPilot.Active)
            {
                Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = free;
            }
        }

        bool AllowQuit()
        {
            _quitting = true;
            ++_joinAttempt;
            return true;
        }

        void OnDestroy()
        {
            _quitting = true;
            ++_joinAttempt;
            if (_timeFrozen) Time.timeScale = 1f;
            Application.wantsToQuit -= AllowQuit;
            if (_network != null)
            {
                _network.OnServerStarted -= OnServerStarted;
                _network.OnClientStopped -= OnClientStopped;
            }
            Session?.Lan.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
