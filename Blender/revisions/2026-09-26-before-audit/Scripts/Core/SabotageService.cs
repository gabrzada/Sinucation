using System;
using System.Collections.Generic;
using FriendSlop.Core.Modifiers;
using UnityEngine;

namespace FriendSlop.Core
{
    public struct PurchaseRequest
    {
        /// <summary>ID idempotente gerado pelo cliente (MULT-013).</summary>
        public string ClientRequestId;
        public int BuyerId;
        public int TargetId;
        /// <summary>0 = Travessura (barata), 1 = Caos (cara).</summary>
        public int Tier;
    }

    /// <summary>Motivo de um alvo não ser válido (UI-006). Compacto para replicação.</summary>
    public enum SabotageBlock : byte
    {
        None,
        ShopClosed,
        BuyerEliminated,
        InvalidTarget,
        Self,
        TargetEliminated,
        AlreadyBought,
        TargetImmune,
        NotEnoughCoins,
        NoValidEffects
    }

    public enum PurchaseStatus
    {
        Accepted,
        Rejected,
        Duplicate
    }

    public sealed class PurchaseResult
    {
        public string RequestId;
        public PurchaseStatus Status;
        public string Reason;
        public int BuyerId;
        public int TargetId;
        public int Cost;
        public int Tier;
        public string EventId;
        public ModifierInstance Applied;

        public bool Accepted => Status == PurchaseStatus.Accepted;

        public PurchaseResult AsDuplicate()
        {
            var copy = (PurchaseResult)MemberwiseClone();
            copy.Status = PurchaseStatus.Duplicate;
            return copy;
        }
    }

    /// <summary>
    /// Validação e execução de sabotagens (SAB-001..009). O comprador escolhe o alvo, o servidor sorteia
    /// o efeito. Compras duplicadas devolvem o resultado anterior sem voltar a debitar.
    /// </summary>
    public sealed class SabotageService
    {
        readonly MatchConfig _config;
        readonly EconomyService _economy;
        readonly ModifierService _modifiers;
        readonly RandomService _rng;
        readonly MatchEventLog _log;
        readonly Func<int, PlayerState> _getPlayer;
        readonly Dictionary<int, int> _purchasesThisWindow = new Dictionary<int, int>();
        readonly Dictionary<string, PurchaseResult> _processed = new Dictionary<string, PurchaseResult>();
        readonly Dictionary<int, float> _nextBuyAt = new Dictionary<int, float>();
        readonly Dictionary<int, float> _immuneUntil = new Dictionary<int, float>();

        public SabotageService(MatchConfig config, EconomyService economy, ModifierService modifiers, RandomService rng,
            MatchEventLog log, Func<int, PlayerState> getPlayer)
        {
            _config = config;
            _economy = economy;
            _modifiers = modifiers;
            _rng = rng;
            _log = log;
            _getPlayer = getPlayer;
        }

        public int WindowIndex { get; private set; }
        public bool IsOpen { get; private set; }

        /// <summary>Todos os jogadores da partida (para o desconto de quem vai atrás). Opcional.</summary>
        public IReadOnlyList<PlayerState> AllPlayers { get; set; }

        public const int TierCount = 2;

        /// <summary>Relógio da partida (segundos), actualizado pelo director — usado no modo sempre aberto.</summary>
        public float Now { get; set; }

        /// <summary>Máquina aberta durante toda a partida: recarga por comprador e imunidade temporária do alvo.</summary>
        public bool Continuous => _config.SabotageAnytime;

        public float CooldownFor(int buyerId) => _nextBuyAt.TryGetValue(buyerId, out float t) ? Mathf.Max(0f, t - Now) : 0f;
        public float ImmunityFor(int targetId) => _immuneUntil.TryGetValue(targetId, out float t) ? Mathf.Max(0f, t - Now) : 0f;

        public void OpenWindow(int index)
        {
            WindowIndex = index;
            IsOpen = true;
            _purchasesThisWindow.Clear();
        }

        public void CloseWindow() => IsOpen = false;

        public int PurchasesThisWindow(int buyerId)
        {
            return _purchasesThisWindow.TryGetValue(buyerId, out int count) ? count : 0;
        }

        /// <summary>
        /// Custo: base (Travessura) ou base × multiplicador (Caos); SAB-007: repetir o mesmo alvo em janelas
        /// consecutivas fica mais caro; quem vai muito atrás no placar tem desconto (comeback).
        /// </summary>
        public int CostFor(PlayerState buyer, PlayerState target, int tier = 0)
        {
            float cost = _config.SabotageCost * (tier >= 1 ? _config.ChaosCostMultiplier : 1f);
            bool repeat = Continuous ? buyer.LastTargetId == target.Id : buyer.LastTargetId == target.Id && buyer.LastTargetWindow == WindowIndex - 1;
            if (repeat) cost *= _config.RepeatTargetCostMultiplier;
            if (IsUnderdog(buyer)) cost *= 1f - _config.UnderdogDiscount;
            return Mathf.CeilToInt(cost);
        }

        public bool IsUnderdog(PlayerState buyer)
        {
            if (AllPlayers == null || _config.UnderdogDiscount <= 0f) return false;
            int best = 0, worst = int.MaxValue;
            foreach (var p in AllPlayers)
            {
                if (!p.IsActive) continue;
                best = Mathf.Max(best, p.RoundWins);
                worst = Mathf.Min(worst, p.RoundWins);
            }
            return buyer.RoundWins == worst && best - worst >= _config.UnderdogGap;
        }

        /// <summary>Devolve null se válido, caso contrário o motivo legível (UI-006).</summary>
        public string ValidateTarget(PlayerState buyer, PlayerState target, int tier = 0)
        {
            var block = ValidateTargetCode(buyer, target, tier);
            return block == SabotageBlock.None ? null : Describe(block);
        }

        public SabotageBlock ValidateTargetCode(PlayerState buyer, PlayerState target, int tier = 0)
        {
            if (!IsOpen) return SabotageBlock.ShopClosed;
            if (buyer == null || !buyer.IsActive) return SabotageBlock.BuyerEliminated;
            if (target == null) return SabotageBlock.InvalidTarget;
            if (target.Id == buyer.Id) return SabotageBlock.Self;
            if (!target.IsActive) return SabotageBlock.TargetEliminated;
            if (Continuous)
            {
                if (CooldownFor(buyer.Id) > 0f) return SabotageBlock.AlreadyBought;
                if (ImmunityFor(target.Id) > 0f) return SabotageBlock.TargetImmune;
            }
            else
            {
                if (PurchasesThisWindow(buyer.Id) >= _config.MaxPurchasesPerWindow) return SabotageBlock.AlreadyBought;
                if (target.IsImmune(WindowIndex)) return SabotageBlock.TargetImmune;
            }
            if (buyer.Coins < CostFor(buyer, target, tier)) return SabotageBlock.NotEnoughCoins;
            if (!_modifiers.HasValidOption(target, tier)) return SabotageBlock.NoValidEffects;
            return SabotageBlock.None;
        }

        public static string Describe(SabotageBlock block)
        {
            switch (block)
            {
                case SabotageBlock.ShopClosed: return "Loja fechada";
                case SabotageBlock.BuyerEliminated: return "Comprador eliminado";
                case SabotageBlock.InvalidTarget: return "Alvo inválido";
                case SabotageBlock.Self: return "Não te podes sabotar";
                case SabotageBlock.TargetEliminated: return "Alvo eliminado";
                case SabotageBlock.AlreadyBought: return "Máquina a recarregar";
                case SabotageBlock.TargetImmune: return "Alvo imune";
                case SabotageBlock.NotEnoughCoins: return "Saldo insuficiente";
                case SabotageBlock.NoValidEffects: return "Sem efeitos válidos";
                default: return null;
            }
        }

        public bool CanBuyAnything(PlayerState buyer, IReadOnlyList<PlayerState> players)
        {
            foreach (var target in players)
                for (int tier = 0; tier < TierCount; tier++)
                    if (ValidateTargetCode(buyer, target, tier) == SabotageBlock.None)
                        return true;
            return false;
        }

        public PurchaseResult Submit(PurchaseRequest request, int round)
        {
            string requestId = string.IsNullOrEmpty(request.ClientRequestId)
                ? "anon-" + request.BuyerId + "-" + WindowIndex
                : request.ClientRequestId;

            if (_processed.TryGetValue(requestId, out var previous))
                return previous.AsDuplicate();

            var buyer = _getPlayer(request.BuyerId);
            var target = _getPlayer(request.TargetId);
            var result = new PurchaseResult
            {
                RequestId = requestId,
                BuyerId = request.BuyerId,
                TargetId = request.TargetId,
                Tier = Mathf.Clamp(request.Tier, 0, TierCount - 1),
                Status = PurchaseStatus.Rejected
            };
            int tier = result.Tier;

            string reason = ValidateTarget(buyer, target, tier);
            if (reason != null)
            {
                result.Reason = reason;
                return Finish(result);
            }

            int cost = CostFor(buyer, target, tier);
            if (!_economy.TryDebit(buyer, cost, (tier >= 1 ? "Caos contra " : "Travessura contra ") + target.Name))
            {
                result.Reason = "Saldo insuficiente";
                return Finish(result);
            }

            string eventId = _rng.NewEventId("SAB");
            var applied = _modifiers.RollAndApply(target, buyer.Id, eventId, round, tier);
            if (applied == null)
            {
                _economy.Refund(buyer, cost, "Reembolso: sem efeitos válidos");
                result.Reason = "Sem efeitos válidos";
                result.EventId = eventId;
                return Finish(result);
            }

            _purchasesThisWindow[buyer.Id] = PurchasesThisWindow(buyer.Id) + 1;
            if (Continuous)
            {
                _nextBuyAt[buyer.Id] = Now + Mathf.Max(0f, _config.SabotageCooldownSeconds);
                _immuneUntil[target.Id] = Now + Mathf.Max(0f, _config.SabotageImmunitySeconds);
            }
            target.ImmuneUntilWindow = WindowIndex + Math.Max(0, _config.ImmunityWindowsAfterSabotage);
            buyer.LastTargetId = target.Id;
            buyer.LastTargetWindow = WindowIndex;
            buyer.SabotagesDone++;
            target.SabotagesReceived++;

            result.Status = PurchaseStatus.Accepted;
            result.Cost = cost;
            result.EventId = eventId;
            result.Applied = applied;
            return Finish(result);
        }

        PurchaseResult Finish(PurchaseResult result)
        {
            _processed[result.RequestId] = result;
            _log?.Record("sabotage_purchase", result.EventId ?? result.RequestId,
                "request=" + result.RequestId + " buyer=" + result.BuyerId + " target=" + result.TargetId +
                " status=" + result.Status + " tier=" + result.Tier + " cost=" + result.Cost +
                (result.Applied != null ? " def=" + result.Applied.Definition.Id : "") +
                (result.Reason != null ? " reason=" + result.Reason : ""));
            return result;
        }
    }
}
