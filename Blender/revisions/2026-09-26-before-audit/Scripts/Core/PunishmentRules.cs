using System.Collections.Generic;

namespace FriendSlop.Core
{
    /// <summary>
    /// EligiblePunishmentPlayers = ActivePlayers − RoundWinner − ProtectedPlayers (PUN-001).
    /// Se a protecção esvaziar a lista, é ignorada só para este sorteio obrigatório.
    /// </summary>
    public static class PunishmentRules
    {
        public static List<int> BuildEligible(IReadOnlyList<PlayerState> players, int roundWinnerId, int currentWindow, bool immunityProtects)
        {
            var eligible = new List<int>();
            foreach (var p in players)
            {
                if (!p.IsActive || p.Id == roundWinnerId) continue;
                if (immunityProtects && p.IsImmune(currentWindow)) continue;
                eligible.Add(p.Id);
            }

            if (eligible.Count == 0 && immunityProtects)
            {
                foreach (var p in players)
                    if (p.IsActive && p.Id != roundWinnerId)
                        eligible.Add(p.Id);
            }
            return eligible;
        }

        /// <summary>TURN-002: ordem da ronda roda o jogador de abertura entre os participantes.</summary>
        public static List<int> RotatedOrder(IReadOnlyList<int> baseOrder, ICollection<int> participants, ref int starterCursor)
        {
            var order = new List<int>();
            if (baseOrder.Count == 0) return order;

            int start = -1;
            for (int step = 1; step <= baseOrder.Count; step++)
            {
                int index = (starterCursor + step) % baseOrder.Count;
                if (participants.Contains(baseOrder[index]))
                {
                    start = index;
                    break;
                }
            }
            if (start < 0) return order;

            starterCursor = start;
            for (int i = 0; i < baseOrder.Count; i++)
            {
                int id = baseOrder[(start + i) % baseOrder.Count];
                if (participants.Contains(id)) order.Add(id);
            }
            return order;
        }
    }
}
