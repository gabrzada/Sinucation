using System;

namespace FriendSlop.Core
{
    /// <summary>
    /// Carteira match-local autoritativa (ECON-001..008). Débitos só acontecem se houver saldo,
    /// numa única operação; créditos respeitam o cap e reportam o motivo.
    /// </summary>
    public sealed class EconomyService
    {
        readonly int _cap;

        public EconomyService(int coinCap)
        {
            _cap = coinCap > 0 ? coinCap : int.MaxValue;
        }

        /// <summary>Jogador, variação efectiva, motivo.</summary>
        public event Action<PlayerState, int, string> BalanceChanged;

        public int Credit(PlayerState player, int amount, string reason)
        {
            if (amount <= 0) return 0;
            int before = player.Coins;
            player.Coins = Math.Min(_cap, before + amount);
            int delta = player.Coins - before;
            player.CoinsEarned += delta;
            player.CoinsLostToCap += amount - delta;
            BalanceChanged?.Invoke(player, delta, delta < amount ? reason + " (saldo máximo)" : reason);
            return delta;
        }

        public bool TryDebit(PlayerState player, int amount, string reason)
        {
            if (amount < 0 || player.Coins < amount) return false;
            player.Coins -= amount;
            player.CoinsSpent += amount;
            BalanceChanged?.Invoke(player, -amount, reason);
            return true;
        }

        /// <summary>Reembolso de uma compra rejeitada depois do débito; ignora o cap para ser exactamente inverso.</summary>
        public void Refund(PlayerState player, int amount, string reason)
        {
            if (amount <= 0) return;
            player.Coins += amount;
            player.CoinsSpent -= amount;
            BalanceChanged?.Invoke(player, amount, reason);
        }

        /// <summary>Moedas de um eliminado são destruídas, nunca transferidas (anti-snowball).</summary>
        public void Wipe(PlayerState player, string reason)
        {
            int amount = player.Coins;
            if (amount <= 0) return;
            player.Coins = 0;
            BalanceChanged?.Invoke(player, -amount, reason);
        }
    }
}
