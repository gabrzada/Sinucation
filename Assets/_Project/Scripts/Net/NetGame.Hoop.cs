using System;
using FriendSlop.Core;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>Um objecto atirado ([G]) tal como o anfitrião o aceitou.</summary>
    public struct ThrowInfo
    {
        public ulong Sender;
        /// <summary>Id do lançamento atribuído pelo anfitrião (igual em todos os peers).</summary>
        public int Id;
        public Vector3 Origin;
        public Vector3 Velocity;
        /// <summary>Voo já decorrido no relógio de rede quando este peer o recebeu (compensação de latência).</summary>
        public float Age;
    }

    /// <summary>
    /// Objectos atirados e cesta de arcade móvel. O anfitrião numera cada arremesso e carimba-o com o tempo de rede; cada
    /// peer simula o objecto em PhysX local, adiantado pelo tempo de voo que já passou, e vê o aro no mesmo sítio porque o
    /// aro se move em função do mesmo relógio. Só a cópia do anfitrião decide a cesta, uma vez por lançamento.
    /// </summary>
    public sealed partial class NetGame
    {
        /// <summary>Atraso máximo que um peer compensa ao fazer nascer um objecto atirado (s).</summary>
        public const float MaxThrowCatchUp = 0.35f;

        readonly HoopLedger _hoop = new HoopLedger();

        public event Action<ThrowInfo> Thrown;
        /// <summary>Cesta validada pelo anfitrião: quem atirou, id do lançamento e Dinheiros creditados (0 = sem prémio).</summary>
        public event Action<ulong, int, int> Basket;

        /// <summary>Relógio de rede partilhado (fora de uma sessão, o tempo local).</summary>
        public double NetworkTime => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        [Rpc(SendTo.Server)]
        void ThrowRpc(Vector3 origin, Vector3 velocity, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (_lastThrow.TryGetValue(sender, out float last) && Time.time - last < 0.9f) return;
            _lastThrow[sender] = Time.time;
            double now = NetworkTime;
            ThrowBroadcastRpc(sender, _hoop.Register(sender, now), origin, Vector3.ClampMagnitude(velocity, 12f), now);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ThrowBroadcastRpc(ulong sender, int id, Vector3 origin, Vector3 velocity, double serverTime)
        {
            float age = IsServer ? 0f : Mathf.Clamp((float)(NetworkTime - serverTime), 0f, MaxThrowCatchUp);
            Thrown?.Invoke(new ThrowInfo { Sender = sender, Id = id, Origin = origin, Velocity = velocity, Age = age });
        }

        /// <summary>
        /// Só no anfitrião: o detector da cesta viu o lançamento <paramref name="throwId"/> entrar no aro por cima. Cada
        /// lançamento conta uma vez; o prémio vai para quem o atirou, pela economia da partida (MatchDirector.CreditBasket).
        /// </summary>
        public bool ServerReportBasket(int throwId)
        {
            if (!IsServer || !IsSpawned) return false;
            if (!_hoop.TryClaim(throwId, NetworkTime, out ulong thrower)) return false;
            int credited = 0;
            int player = PlayerOf(thrower);
            if (_director != null && player >= 0)
            {
                credited = _director.CreditBasket(player);
                if (credited > 0) _dirty = true;
            }
            Debug.Log("[Hoop] cesta lançamento=" + throwId + " cliente=" + thrower + " jogador=" + player + " +" + credited);
            BasketRpc(thrower, throwId, credited);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BasketRpc(ulong thrower, int throwId, int credited) => Basket?.Invoke(thrower, throwId, credited);
    }
}
