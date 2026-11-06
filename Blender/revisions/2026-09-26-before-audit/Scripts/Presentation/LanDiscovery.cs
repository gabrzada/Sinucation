using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Uma sala visível no navegador (online ou na rede local).</summary>
    public sealed class RoomListing
    {
        public string Name;
        public string Address;
        public ushort Port;
        public string SessionId;
        public int Players;
        public int MaxPlayers;
        public bool InMatch;
        public bool Online;
        public float SeenAt;
    }

    /// <summary>
    /// Descoberta de salas públicas na rede local por broadcast UDP. O anfitrião anuncia a cada segundo; os
    /// clientes no menu escutam. Salas privadas nunca são anunciadas (entra-se só com código/IP).
    /// </summary>
    public sealed class LanDiscovery : IDisposable
    {
        public const int DiscoveryPort = 47779;
        const string Magic = "FSLOP";

        UdpClient _listener;
        UdpClient _broadcaster;
        float _nextBroadcast;
        readonly Dictionary<string, RoomListing> _rooms = new Dictionary<string, RoomListing>();

        public IEnumerable<RoomListing> Rooms
        {
            get
            {
                float now = Time.unscaledTime;
                foreach (var r in _rooms.Values)
                    if (now - r.SeenAt < 4f) yield return r;
            }
        }

        public void StartListening()
        {
            if (_listener != null) return;
            try
            {
                _listener = new UdpClient();
                _listener.ExclusiveAddressUse = false;
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _listener.Client.Blocking = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FriendSlop] Descoberta LAN indisponível: " + e.Message);
                _listener = null;
            }
        }

        public void StopListening()
        {
            _listener?.Close();
            _listener = null;
            _rooms.Clear();
        }

        public void Poll()
        {
            if (_listener == null) return;
            try
            {
                while (_listener.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _listener.Receive(ref from);
                    string[] parts = Encoding.UTF8.GetString(data).Split('|');
                    if (parts.Length < 7 || parts[0] != Magic) continue;
                    ushort.TryParse(parts[2], out ushort port);
                    int.TryParse(parts[3], out int players);
                    int.TryParse(parts[4], out int max);
                    string key = from.Address + ":" + port;
                    if (!_rooms.TryGetValue(key, out var room)) _rooms[key] = room = new RoomListing();
                    room.Name = parts[1];
                    room.Address = from.Address.ToString();
                    room.Port = port;
                    room.Players = players;
                    room.MaxPlayers = max;
                    room.InMatch = parts[5] == "1";
                    room.Online = false;
                    room.SeenAt = Time.unscaledTime;
                }
            }
            catch (SocketException)
            {
                // Sem dados agora: normal num socket não bloqueante.
            }
        }

        /// <summary>Chamado pelo anfitrião de uma sala LAN pública.</summary>
        public void Announce(string name, ushort port, int players, int maxPlayers, bool inMatch)
        {
            if (Time.unscaledTime < _nextBroadcast) return;
            _nextBroadcast = Time.unscaledTime + 1f;
            try
            {
                if (_broadcaster == null)
                {
                    _broadcaster = new UdpClient();
                    _broadcaster.EnableBroadcast = true;
                }
                string safeName = (name ?? "Sala").Replace("|", "/");
                byte[] payload = Encoding.UTF8.GetBytes(Magic + "|" + safeName + "|" + port + "|" + players + "|" + maxPlayers + "|" + (inMatch ? "1" : "0") + "|v3");
                _broadcaster.Send(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                _broadcaster.Send(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
            }
            catch (Exception)
            {
                // Rede sem broadcast (ex.: VPN): a sala continua acessível por IP.
            }
        }

        public void Dispose()
        {
            StopListening();
            _broadcaster?.Close();
            _broadcaster = null;
        }
    }
}
