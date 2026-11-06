using System;
using System.Collections.Generic;

namespace FriendSlop.Core.Modifiers
{
    /// <summary>
    /// Sorteia e aplica modificadores válidos (PUN-008, SAB-004). O peso efectivo é
    /// BaseWeight × AntiRepeat; opções que seriam rejeitadas pelo controller ficam fora do sorteio.
    /// </summary>
    public sealed class ModifierService
    {
        readonly List<ModifierDefinition> _catalog;
        readonly MatchConfig _config;
        readonly RandomService _rng;
        readonly MatchEventLog _log;
        int _nextInstanceId;

        public ModifierService(IReadOnlyList<ModifierDefinition> catalog, MatchConfig config, RandomService rng, MatchEventLog log)
        {
            _catalog = new List<ModifierDefinition>();
            if (catalog != null)
                foreach (var def in catalog)
                    if (def != null && def.ProbabilityWeight > 0f)
                        _catalog.Add(def);
            _config = config;
            _rng = rng;
            _log = log;
        }

        public IReadOnlyList<ModifierDefinition> Catalog => _catalog;

        public event Action<PlayerState, ModifierInstance> Applied;

        /// <param name="tier">-1 = qualquer nível (risco); 0/1 = só efeitos desse nível de sabotagem.</param>
        public List<ModifierDefinition> ValidOptions(PlayerState target, int tier = -1)
        {
            var options = new List<ModifierDefinition>();
            foreach (var def in _catalog)
                if ((tier < 0 || (int)def.Tier == tier) && target.Modifiers.CanApply(def, _config.MaxGameplayModifiersPerPlayer, out _))
                    options.Add(def);
            return options;
        }

        public bool HasValidOption(PlayerState target, int tier = -1)
        {
            foreach (var def in _catalog)
                if ((tier < 0 || (int)def.Tier == tier) && target.Modifiers.CanApply(def, _config.MaxGameplayModifiersPerPlayer, out _))
                    return true;
            return false;
        }

        public float EffectiveWeight(ModifierDefinition def, PlayerState target)
        {
            float weight = def.ProbabilityWeight;
            foreach (var recent in target.RecentModifierIds)
                if (recent == def.Id)
                    weight *= _config.AntiRepeatWeightMultiplier;
            return weight;
        }

        public ModifierInstance RollAndApply(PlayerState target, int sourcePlayerId, string eventId, int round, int tier = -1)
        {
            var options = ValidOptions(target, tier);
            if (options.Count == 0)
            {
                _log?.Record("modifier_roll", eventId, "target=" + target.Id + " no_valid_options");
                return null;
            }

            var def = _rng.ResolveWeightedRoll(eventId, "modifier_roll", options, d => EffectiveWeight(d, target), d => d.Id);
            var instance = target.Modifiers.Apply(def, target.Id, sourcePlayerId, eventId, round, ++_nextInstanceId,
                _config.MaxGameplayModifiersPerPlayer);
            if (instance == null) return null;

            target.RememberModifier(def.Id);
            _log?.Record("modifier_applied", eventId,
                "target=" + target.Id + " source=" + sourcePlayerId + " def=" + def.Id + " intensity=" + instance.Intensity);
            Applied?.Invoke(target, instance);
            return instance;
        }
    }
}
