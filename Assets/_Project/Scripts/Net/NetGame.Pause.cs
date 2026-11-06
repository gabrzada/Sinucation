using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Quando existe a pausa: numa partida em que o anfitrião é o único humano (os outros lugares são bots) e não há mais
    /// ninguém ligado à sala, nem espectadores nem humanos desligados com o lugar reservado.
    /// </summary>
    public static class SoloPause
    {
        /// <param name="connectedClients">Ligações à sala, contando com o próprio anfitrião.</param>
        /// <param name="hostSeated">O anfitrião tem lugar nesta partida.</param>
        /// <param name="otherHumanSeats">Outros lugares humanos ainda em jogo (ligados ou à espera de voltar).</param>
        public static bool Allowed(bool inMatch, int connectedClients, bool hostSeated, int otherHumanSeats) =>
            inMatch && connectedClients == 1 && hostSeated && otherHumanSeats == 0;

        /// <summary>
        /// Alguém tenta entrar durante a pausa: recusado (ver Docs/DECISOES.md). Retomar sozinho deixava o relógio do turno,
        /// a roleta e os bots a correr contra quem foi fazer a pausa; quem entra só ia ver, e pode tentar de novo.
        /// </summary>
        public static bool RefusesJoin(bool paused) => paused;

        public const string JoinRefusedReason = "A partida está em pausa: o anfitrião está a jogar sozinho contra bots. Tenta entrar quando ele a retomar.";
    }

    public sealed partial class NetGame
    {
        /// <summary>Partida congelada pelo único humano da sala.</summary>
        public bool MatchPaused => HasSnapshot && View.InMatch && View.MatchPaused;
        /// <summary>Este processo é o anfitrião sozinho contra bots: pode pausar.</summary>
        public bool CanPauseMatch => IsLocalHost && HasSnapshot && View.InMatch && View.PauseAllowed;
        /// <summary>Entradas recusadas por a partida estar em pausa (QA).</summary>
        public int RefusedJoins { get; private set; }

        public void RequestMatchPause(bool paused) => PauseRpc(paused);

        bool SoloPauseAllowed
        {
            get
            {
                if (_director == null || !IsLocalHost) return false;
                ulong host = NetworkManager.LocalClientId;
                bool hostSeated = false;
                int others = 0;
                for (int i = 0; i < _seatSlots.Count; i++)
                {
                    var s = _seatSlots[i];
                    if (s.Profile.IsBot) continue;
                    if (s.Connected && s.ClientId == host)
                    {
                        hostSeated = true;
                        continue;
                    }
                    // Quem saiu e já foi eliminado (abandono) não volta a jogar; quem tem o lugar reservado conta.
                    var player = _director.GetPlayer(i);
                    if (s.Connected || (player != null && !player.Eliminated)) others++;
                }
                return SoloPause.Allowed(true, NetworkManager.ConnectedClientsIds.Count, hostSeated, others);
            }
        }

        [Rpc(SendTo.Server)]
        void PauseRpc(bool paused, RpcParams rpcParams = default)
        {
            if (!IsHostSender(rpcParams.Receive.SenderClientId) || _director == null) return;
            if (paused && !SoloPauseAllowed)
            {
                Toast?.Invoke("A pausa só existe quando jogas sozinho contra bots.");
                return;
            }
            SetMatchPaused(paused, "anfitrião");
        }

        void SetMatchPaused(bool paused, string reason)
        {
            if (_director == null || !_director.SetPaused(paused, reason)) return;
            _dirty = true;
            Debug.Log("[Pause] " + (paused ? "pausa" : "retoma") + " (" + reason + ") fase=" + _director.Phase + " resta=" + _director.PhaseTimeRemaining.ToString("0.00") +
                      " s, tempo de jogo=" + _director.MatchTime.ToString("0.00") + " s");
        }

        /// <summary>Rede de segurança: se a sala deixar de ser "um humano contra bots", a pausa acaba.</summary>
        void UpdatePause()
        {
            if (_director == null || !_director.Paused || SoloPauseAllowed) return;
            SetMatchPaused(false, "já não está sozinho contra bots");
            BroadcastToast("A partida retomou: já não estás sozinho contra bots.");
        }

        /// <summary>Aprovação de ligação no anfitrião: devolve o motivo da recusa, ou null se pode entrar.</summary>
        public string JoinRefusal(string name)
        {
            if (!IsServer || _director == null || !SoloPause.RefusesJoin(_director.Paused)) return null;
            NoteRefusedJoin(name);
            return SoloPause.JoinRefusedReason;
        }

        /// <summary>Aprovado mesmo antes da pausa e ligado já durante ela: recusa da mesma forma.</summary>
        bool RefuseJoinWhilePaused(ulong clientId)
        {
            if (_director == null || !SoloPause.RefusesJoin(_director.Paused) || clientId == NetworkManager.LocalClientId) return false;
            PendingJoins.TryGetValue(clientId, out var payload);
            PendingJoins.Remove(clientId);
            NoteRefusedJoin(payload.Profile?.Name);
            NetworkManager.DisconnectClient(clientId, SoloPause.JoinRefusedReason);
            return true;
        }

        void NoteRefusedJoin(string name)
        {
            RefusedJoins++;
            Debug.Log("[Pause] entrada recusada durante a pausa: " + (string.IsNullOrEmpty(name) ? "?" : name));
            Toast?.Invoke((string.IsNullOrEmpty(name) ? "Alguém" : name) + " tentou entrar, mas a partida está em pausa. Retoma (P) para deixar entrar.");
        }
    }
}
