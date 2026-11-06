using System;
using System.Collections.Generic;
using FriendSlop.Match;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Interacções sociais que não mexem na partida: marcadores (botão do meio) e o modo de brincadeira depois do
    /// placar final (o vencedor dispara contra quem perdeu; quem leva um tiro cai em ragdoll ~1 s e levanta-se).
    /// O anfitrião valida frequência e permissões; nada disto toca em pontos, moedas, resultado ou recompensas.
    /// </summary>
    public sealed partial class NetGame
    {
        // =================================================================== marcadores

        /// <summary>Marcador de alguém da sala: quem, ponto (mundo) e normal da superfície.</summary>
        public event Action<ulong, Vector3, Vector3> Ping;
        /// <summary>O anfitrião recusou um marcador deste cliente (ainda em recarga): segundos que faltam.</summary>
        public event Action<float> PingRefused;

        readonly Dictionary<ulong, float> _lastPing = new Dictionary<ulong, float>();
        float _localPingReadyAt;

        public static float PingCooldownSeconds => SharedConfig != null ? Mathf.Max(0.3f, SharedConfig.PingCooldownSeconds) : 2.5f;
        public static float PingDurationSeconds => SharedConfig != null ? Mathf.Clamp(SharedConfig.PingDurationSeconds, 0.5f, 15f) : 3.5f;

        /// <summary>Recarga do marcador vista por este cliente (previsão local; o anfitrião tem a última palavra).</summary>
        public float PingCooldownRemaining => Mathf.Max(0f, _localPingReadyAt - Time.unscaledTime);

        public bool RequestPing(Vector3 point, Vector3 normal)
        {
            if (PingCooldownRemaining > 0f) return false;
            _localPingReadyAt = Time.unscaledTime + PingCooldownSeconds;
            PingRpc(point, normal);
            return true;
        }

        /// <summary>QA: envia um marcador sem a recarga local (simula um cliente alterado; o anfitrião tem de recusar).</summary>
        public void DebugPingUnchecked(Vector3 point) => PingRpc(point, Vector3.up);

        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        [Rpc(SendTo.Server)]
        void PingRpc(Vector3 point, Vector3 normal, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!Finite(point) || point.sqrMagnitude > 40f * 40f) return;
            float now = Time.unscaledTime;
            float cooldown = PingCooldownSeconds;
            // Pequena tolerância para a variação da rede; um cliente alterado não consegue mais do que isto.
            if (_lastPing.TryGetValue(sender, out float last) && now - last < cooldown - 0.1f)
            {
                PingRefusedRpc(cooldown - (now - last), RpcTarget.Single(sender, RpcTargetUse.Temp));
                return;
            }
            _lastPing[sender] = now;
            if (!Finite(normal) || normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
            PingBroadcastRpc(sender, point, normal.normalized);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void PingBroadcastRpc(ulong sender, Vector3 point, Vector3 normal) => Ping?.Invoke(sender, point, normal);

        [Rpc(SendTo.SpecifiedInParams)]
        void PingRefusedRpc(float remaining, RpcParams rpcParams)
        {
            _localPingReadyAt = Time.unscaledTime + Mathf.Max(0f, remaining);
            PingRefused?.Invoke(remaining);
        }

        // =================================================================== brincadeira depois da partida

        public const float FunShotCooldown = 0.45f;
        public const float FunRagdollSeconds = 1.1f;
        /// <summary>Depois de um tiro o alvo fica uns instantes sem levar outro (as forças nunca se acumulam).</summary>
        public const float FunImmunitySeconds = 1.6f;

        /// <summary>Disparo no modo de brincadeira: atirador, alvo (−1 = falhou), origem e ponto de impacto.</summary>
        public event Action<int, int, Vector3, Vector3> FunShot;

        /// <summary>Posição (pés) do boneco de um jogador, dada pela apresentação do anfitrião para validar tiros.</summary>
        public Func<int, Vector3?> FunTargetPosition { get; set; }

        readonly Dictionary<int, float> _funHitAt = new Dictionary<int, float>();
        readonly System.Random _funRng = new System.Random();
        float _funShotAt = -10f;
        float _funBotTimer = 5f;

        /// <summary>Partida acabada com vencedor: a sala fica em modo livre até à revanche ou ao regresso à sala.</summary>
        public bool FunModeActive => HasSnapshot && View.InMatch && View.Phase == MatchPhase.MatchOver && View.MatchWinner >= 0;
        public bool LocalIsFunShooter => FunModeActive && LocalPlayer != null && LocalPlayer.Id == View.MatchWinner;

        void ResetFunMode()
        {
            _funHitAt.Clear();
            _funShotAt = -10f;
            _funBotTimer = 5f;
        }

        public void RequestFunShot(Vector3 origin, Vector3 direction, int target, Vector3 hitPoint) => FunShotRpc(origin, direction, target, hitPoint);

        [Rpc(SendTo.Server)]
        void FunShotRpc(Vector3 origin, Vector3 direction, int target, Vector3 hitPoint, RpcParams rpcParams = default)
        {
            if (_director == null || _director.Phase != MatchPhase.MatchOver || _director.Paused) return;
            int shooter = PlayerOf(rpcParams.Receive.SenderClientId);
            if (shooter < 0 || shooter != _director.MatchWinnerId) return;
            ServerFunShot(shooter, origin, direction, target, hitPoint);
        }

        void ServerFunShot(int shooter, Vector3 origin, Vector3 direction, int target, Vector3 hitPoint)
        {
            float now = Time.time;
            if (now - _funShotAt < FunShotCooldown - 0.05f) return;
            if (!Finite(origin) || !Finite(direction) || !Finite(hitPoint) || direction.sqrMagnitude < 1e-6f) return;
            // A origem tem de ser o vencedor (olhos/arma); um cliente alterado não dispara do outro lado da sala.
            var shooterBody = FunTargetPosition?.Invoke(shooter);
            if (shooterBody != null && (origin - (shooterBody.Value + Vector3.up * 1.05f)).sqrMagnitude > 1.6f * 1.6f) return;
            _funShotAt = now;
            direction.Normalize();
            if (!ValidFunTarget(shooter, target, origin, direction, hitPoint, now)) target = -1;
            if (target >= 0) _funHitAt[target] = now;
            else hitPoint = origin + direction * Mathf.Clamp(Vector3.Distance(origin, hitPoint), 0.5f, 20f);
            FunShotBroadcastRpc(shooter, target, origin, hitPoint);
        }

        bool ValidFunTarget(int shooter, int target, Vector3 origin, Vector3 direction, Vector3 hitPoint, float now)
        {
            if (target < 0 || target == shooter || _director.GetPlayer(target) == null) return false;
            if (_funHitAt.TryGetValue(target, out float last) && now - last < FunImmunitySeconds) return false;
            var body = FunTargetPosition?.Invoke(target);
            if (body == null) return true;
            Vector3 center = body.Value + Vector3.up * 0.7f;
            Vector3 to = center - origin;
            if (to.magnitude > 30f) return false;
            // O impacto tem de estar no boneco e na linha do disparo (um cliente não "acerta" em quem não vê).
            float miss = Vector3.Distance(hitPoint, center);
            if (miss > 1.3f) return false;
            // O ponto de impacto vem do raycast do cliente ao longo da direcção: tem de estar sobre esse raio.
            float along = Vector3.Dot(hitPoint - origin, direction);
            if (along < 0f) return false;
            return (hitPoint - (origin + direction * along)).sqrMagnitude < 0.4f * 0.4f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void FunShotBroadcastRpc(int shooter, int target, Vector3 origin, Vector3 hitPoint) => FunShot?.Invoke(shooter, target, origin, hitPoint);

        /// <summary>Bot vencedor: também brinca, de poucos em poucos segundos, com quem perdeu.</summary>
        void UpdateFunBots(float dt)
        {
            if (_director.Phase != MatchPhase.MatchOver || _director.Paused) return;
            var winner = _director.GetPlayer(_director.MatchWinnerId);
            if (winner == null || !winner.Profile.IsBot) return;
            _funBotTimer -= dt;
            if (_funBotTimer > 0f) return;
            _funBotTimer = 1.8f + (float)_funRng.NextDouble() * 2.4f;
            var from = FunTargetPosition?.Invoke(winner.Id);
            if (from == null) return;
            var options = new List<int>();
            foreach (var p in _director.Players)
                if (p.Id != winner.Id && FunTargetPosition?.Invoke(p.Id) is Vector3 at && (at - from.Value).sqrMagnitude < 14f * 14f) options.Add(p.Id);
            if (options.Count == 0) return;
            int target = options[_funRng.Next(options.Count)];
            Vector3 origin = from.Value + Vector3.up * 1.05f;
            Vector3 center = FunTargetPosition(target).Value + Vector3.up * 0.7f;
            ServerFunShot(winner.Id, origin, center - origin, target, center);
        }
    }
}
