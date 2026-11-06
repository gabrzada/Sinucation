using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FriendSlop.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Camada de sessão (LOB-001..003): sala online (pública ou privada) com código curto via relay, anfitrião
    /// / entrada directa por IP (LAN, com descoberta automática das salas públicas) e treino offline.
    /// Todas acabam no mesmo Netcode com listen server autoritativo.
    /// </summary>
    public sealed class SessionService
    {
        public const ushort DefaultPort = 7777;
        public const int MaxConnections = 8;
        const string StatusKey = "status";

        readonly NetworkManager _network;
        ISession _session;
        bool _lastPublishedInMatch;
        float _nextStatusPush;

        public SessionService(NetworkManager network)
        {
            _network = network;
        }

        public bool Busy { get; private set; }
        public string LastError { get; private set; }
        public ushort HostedPort { get; private set; }
        public LanDiscovery Lan { get; } = new LanDiscovery();
        public readonly List<RoomListing> OnlineRooms = new List<RoomListing>();
        public bool Querying { get; private set; }

        UnityTransport Transport => (UnityTransport)_network.NetworkConfig.NetworkTransport;

        public bool StartOffline(int bots)
        {
            Prepare(SessionKind.Offline, "", bots, "Treino", true);
            ushort port = (ushort)UnityEngine.Random.Range(20000, 40000);
            Transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
            return Report(_network.StartHost(), "Não foi possível iniciar o treino local.");
        }

        public bool HostDirect(ushort port, string listenAddress, int bots, string roomName = "Sala LAN", bool isPrivate = false)
        {
            Prepare(SessionKind.Direct, LocalAddress() + ":" + port, bots, roomName, isPrivate);
            HostedPort = port;
            Transport.SetConnectionData("127.0.0.1", port, string.IsNullOrEmpty(listenAddress) ? "0.0.0.0" : listenAddress);
            return Report(_network.StartHost(), "Não foi possível abrir a porta " + port + ".");
        }

        public bool JoinDirect(string address, ushort port)
        {
            LastError = null;
            Transport.SetConnectionData(string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim(), port);
            return Report(_network.StartClient(), "Não foi possível ligar a " + address + ":" + port + ".");
        }

        public async Task<bool> HostOnline(string roomName, bool isPrivate, int maxPlayers)
        {
            if (Busy) return false;
            Busy = true;
            try
            {
                await EnsureSignedIn();
                Prepare(SessionKind.Online, "", 0, roomName, isPrivate);
                var options = new SessionOptions
                {
                    Name = string.IsNullOrWhiteSpace(roomName) ? "Sala FriendSlop" : roomName.Trim(),
                    MaxPlayers = Mathf.Clamp(maxPlayers + 2, 3, MaxConnections),
                    IsPrivate = isPrivate,
                    SessionProperties = new Dictionary<string, SessionProperty> { { StatusKey, new SessionProperty("lobby") } }
                }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                NetGame.ServerJoinCode = _session.Code ?? "";
                _lastPublishedInMatch = false;
                return true;
            }
            catch (Exception e)
            {
                LastError = "Falha ao criar sala online: " + Friendly(e);
                Debug.LogWarning("[FriendSlop] " + e);
                return false;
            }
            finally
            {
                Busy = false;
            }
        }

        public Task<bool> JoinOnline(string code) => JoinOnlineInternal(code, null);
        public Task<bool> JoinOnlineById(string sessionId) => JoinOnlineInternal(null, sessionId);

        async Task<bool> JoinOnlineInternal(string code, string sessionId)
        {
            if (Busy) return false;
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(sessionId))
            {
                LastError = "Escreve o código da sala.";
                return false;
            }
            Busy = true;
            try
            {
                await EnsureSignedIn();
                _session = sessionId != null
                    ? await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId)
                    : await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                return true;
            }
            catch (Exception e)
            {
                LastError = "Falha ao entrar na sala: " + Friendly(e);
                Debug.LogWarning("[FriendSlop] " + e);
                return false;
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Lista as salas públicas online (servers abertos).</summary>
        public async Task RefreshOnlineRooms()
        {
            if (Querying) return;
            Querying = true;
            try
            {
                await EnsureSignedIn();
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 30 });
                OnlineRooms.Clear();
                foreach (var info in results.Sessions)
                {
                    bool inMatch = info.Properties != null && info.Properties.TryGetValue(StatusKey, out var status) && status.Value == "jogo";
                    OnlineRooms.Add(new RoomListing
                    {
                        Name = info.Name,
                        SessionId = info.Id,
                        Players = info.MaxPlayers - info.AvailableSlots,
                        MaxPlayers = info.MaxPlayers,
                        InMatch = inMatch,
                        Online = true,
                        SeenAt = Time.unscaledTime
                    });
                }
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = "Salas online indisponíveis: " + Friendly(e);
            }
            finally
            {
                Querying = false;
            }
        }

        /// <summary>Anfitrião: publica o estado (sala de espera / em jogo) e anuncia na LAN se for pública.</summary>
        public void HostTick(NetGame net)
        {
            if (net == null || !net.IsLocalHost || !net.HasSnapshot) return;
            var view = net.View;
            if (view.Kind == SessionKind.Direct && !view.IsPrivate)
                Lan.Announce(view.RoomName, HostedPort, view.Slots.Count, view.Settings.MaxPlayers, view.InMatch);

            if (view.Kind == SessionKind.Online && _session is IHostSession host && view.InMatch != _lastPublishedInMatch && Time.unscaledTime > _nextStatusPush)
            {
                _lastPublishedInMatch = view.InMatch;
                _nextStatusPush = Time.unscaledTime + 3f;
                PushStatus(host, view.InMatch);
            }
        }

        static async void PushStatus(IHostSession host, bool inMatch)
        {
            try
            {
                host.SetProperty(StatusKey, new SessionProperty(inMatch ? "jogo" : "lobby"));
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendSlop] Não foi possível actualizar o estado da sala: " + e.Message);
            }
        }

        public async Task Leave()
        {
            var session = _session;
            _session = null;
            if (_network != null && _network.IsListening) _network.Shutdown();
            if (session == null) return;
            try
            {
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendSlop] Ao sair da sessão: " + e.Message);
            }
        }

        void Prepare(SessionKind kind, string code, int bots, string roomName, bool isPrivate)
        {
            LastError = null;
            NetGame.ServerKind = kind;
            NetGame.ServerJoinCode = code;
            NetGame.InitialBots = bots;
            NetGame.ServerRoomName = string.IsNullOrWhiteSpace(roomName) ? "Sala" : roomName.Trim();
            NetGame.ServerIsPrivate = isPrivate;
        }

        bool Report(bool ok, string error)
        {
            if (!ok) LastError = error;
            return ok;
        }

        static async Task EnsureSignedIn()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                // Perfil por processo: permite várias instâncias na mesma máquina (testes locais).
                options.SetProfile("p" + UnityEngine.Random.Range(0, 99999999));
                await UnityServices.InitializeAsync(options);
            }
            while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string Friendly(Exception e)
        {
            string message = e.Message;
            if (message.Contains("project") || message.Contains("Project"))
                return message + " (confirma que o projecto está ligado a um projecto Unity Cloud em Project Settings ▸ Services).";
            return message;
        }

        static string LocalAddress()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }
    }
}
