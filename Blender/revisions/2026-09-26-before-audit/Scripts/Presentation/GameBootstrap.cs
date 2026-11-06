using System.Collections.Generic;
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

        public AppScreen Screen { get; private set; } = AppScreen.Menu;
        public bool Paused { get; private set; }
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
            Instance = this;
            Settings = GameSettings.Load();
            Config = _config != null ? _config : ScriptableObject.CreateInstance<MatchConfig>();
            Catalog = HasValidModifiers() ? (IReadOnlyList<ModifierDefinition>)_modifiers : ModifierCatalog.CreateDefaults();
            VisualFactory.Initialize(_litTemplate, _unlitTemplate);
            VisualFactory.InitializeComplex(_complexLitTemplate);
            VisualFactory.InitializeParticles(_particleTemplate);
            // Só existem para o build manter variantes de shader; referenciá-los evita avisos de campo sem uso.
            if (_litTransparentTemplate == null || _shaderVariantKeepers == null) Debug.Log("[FriendSlop] Materiais de referência em falta: corre FriendSlop ▸ Criar ou reconstruir cena de jogo.");
            NetGame.SharedConfig = Config;
            NetGame.SharedCatalog = Catalog;
            MusicLibrary.Register();

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

            float halfLength = Config.Physics.TableLength * 0.5f;
            float halfWidth = Config.Physics.TableWidth * 0.5f;
            Room = CreateChild<RoomBuilder>("Room");
            Room.Build(halfLength, halfWidth);
            GraphicsQuality.ShadowLights = Room.ShadowLights;
            GraphicsQuality.Dust = Room.Dust;
            GraphicsQuality.Probe = Room.Probe;

            Table = CreateChild<TableView>("Table");
            Table.Initialize(Config.Physics);
            Goo = CreateChild<GooFX>("Goo");
            Goo.Initialize();

            Randomizer = CreateAt<RandomizerMachine>("RandomizerMachine", RoomBuilder.RandomizerAnchor);
            Randomizer.Build(this, Catalog);
            SabotageMachine = CreateAt<SabotageMachine>("SabotageMachine", RoomBuilder.SabotageAnchor);
            SabotageMachine.Build();
            Jukebox = CreateAt<JukeboxStation>("Jukebox", RoomBuilder.JukeboxAnchor);
            Jukebox.Build(this);
            CreateChild<SpectatorCrowd>("Crowd").Build(Room.BalconySpots, GraphicsQuality.CrowdCount);

            Rig = cam.gameObject.AddComponent<FirstPersonRig>();
            Rig.Initialize(this, cam);
            Interaction = CreateChild<InteractionSystem>("Interaction");
            Interaction.Initialize(this);
            Spectator = CreateChild<SpectatorCamera>("Spectator");
            Spectator.Initialize(this);
            Shot = CreateChild<ShotController>("Shot");
            Shot.Initialize(this);
            Cue = CreateChild<CueView>("Cue");
            Cue.Initialize(this);
            Device = CreateChild<RiskDeviceView>("RiskDevice");
            var rest = Randomizer.RestPose;
            Device.Initialize(this, rest.position, rest.rotation);
            CreateChild<NpcAvatars>("Bots");
            Throwables = CreateChild<ThrowablesView>("Throwables");
            Throwables.Initialize(this);
            Sfx = CreateChild<SfxService>("Audio");
            Sfx.Initialize(this);
            Hud = CreateChild<HudView>("Hud");
            Hud.Initialize(this);
            BuildShowcase();

            GraphicsQuality.Apply(cam);
            // Testes automáticos usam -screen-* (janelas lado a lado): não impor o modo de ecrã guardado.
            if (!AutoPilot.HasArguments()) GraphicsQuality.ApplyDisplay();
            SetupNetwork();
            if (AutoPilot.HasArguments()) gameObject.AddComponent<AutoPilot>();
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
            string reason = _network != null ? _network.DisconnectReason : null;
            if (Screen != AppScreen.Menu)
                StatusMessage = string.IsNullOrEmpty(reason) ? (wasHost ? "Sessão terminada." : "Ligação ao anfitrião terminada.") : reason;
            Screen = AppScreen.Menu;
            Paused = false;
            _boundNet = null;
            _pendingSettings = null;
            Interaction.Close();
            NetGame.PendingJoins.Clear();
        }

        void PrepareLocalJoin()
        {
            Settings.Save();
            NetGame.LocalJoin = new JoinPayload { Version = NetGame.ProtocolVersion, Key = Settings.PlayerKey, Profile = LocalProfile };
            _network.NetworkConfig.ConnectionData = NetGame.LocalJoin.Encode();
            StatusMessage = null;
            Paused = false;
        }

        public void StartPractice(int bots)
        {
            PrepareLocalJoin();
            Screen = AppScreen.Connecting;
            StatusMessage = "A preparar o treino…";
            if (!Session.StartOffline(bots)) Fail(Session.LastError);
        }

        /// <summary>Compatibilidade (AutoPilot/testes): sala LAN pública sem regras extra.</summary>
        public void HostLan(ushort port, string listenAddress, int bots)
        {
            PrepareLocalJoin();
            Screen = AppScreen.Connecting;
            if (!Session.HostDirect(port, listenAddress, bots, "Sala de " + LocalProfile.Name)) Fail(Session.LastError);
        }

        /// <summary>Cria sala (online por código, ou LAN) com as regras escolhidas; aplicadas assim que a sala abre.</summary>
        public async void HostRoom(RoomRequest request)
        {
            Settings.LastRoomName = request.Name;
            PrepareLocalJoin();
            Screen = AppScreen.Connecting;
            _pendingSettings = LobbySettings.From(Config);
            _pendingSettings.MaxPlayers = request.MaxPlayers;
            _pendingSettings.RoundLimit = request.Rounds;
            _pendingSettings.BallCount = request.BallCount;
            _pendingSettings.EnableSabotage = request.EnableSabotage;
            _pendingSettings.EnableDebuffs = request.EnableDebuffs;
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

        public void JoinLan(string address, ushort port)
        {
            Settings.LastAddress = address;
            PrepareLocalJoin();
            Screen = AppScreen.Connecting;
            StatusMessage = "A ligar a " + address + ":" + port + "…";
            if (!Session.JoinDirect(address, port)) Fail(Session.LastError);
        }

        public async void JoinOnline(string code)
        {
            Settings.LastCode = code;
            PrepareLocalJoin();
            Screen = AppScreen.Connecting;
            StatusMessage = "A entrar na sala " + code + "…";
            if (!await Session.JoinOnline(code)) Fail(Session.LastError);
        }

        public async void JoinOnlineById(string sessionId, string roomName)
        {
            PrepareLocalJoin();
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
            PrepareLocalJoin();
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

        void Fail(string message)
        {
            if (_network != null && _network.IsListening) _network.Shutdown();
            Screen = AppScreen.Menu;
            StatusMessage = message;
            _pendingSettings = null;
        }

        public async void Leave()
        {
            Paused = false;
            Screen = AppScreen.Menu;
            _boundNet = null;
            _pendingSettings = null;
            Interaction.Close();
            await Session.Leave();
        }

        public void Quit()
        {
            Settings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void TogglePause()
        {
            Paused = !Paused;
            if (!Paused) Settings.Save();
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
                Hud.Bind(net);
                Sfx.Bind(net);
                Device.Bind(net);
                Throwables.Bind(net);
                SabotageMachine.Bind(net);
                Randomizer.Bind(net);
            }

            if (Screen == AppScreen.Connecting && net != null && net.HasSnapshot && NetPlayer.Local != null) Screen = AppScreen.Session;
            if (Screen == AppScreen.Session && (_network == null || !_network.IsListening && !_network.IsConnectedClient)) Screen = AppScreen.Menu;

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
                if (Screen == AppScreen.Session && keyboard.tabKey.wasPressedThisFrame && !Shot.UsesShotCamera && Interaction.Active == null && !Hud.ChatOpen)
                    Hud.PanelCursor = !Hud.PanelCursor;
            }

            UpdateShowcase();

            bool free = Screen != AppScreen.Session || Paused || Hud.WantsCursor || Interaction.Active != null;
            if (AutoPilot.Active) free = false;
            CursorFree = free;
            if (!AutoPilot.Active)
            {
                Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = free;
            }
        }

        void OnDestroy()
        {
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
