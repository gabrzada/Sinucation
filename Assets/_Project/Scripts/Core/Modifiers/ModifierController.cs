using System.Collections.Generic;

namespace FriendSlop.Core.Modifiers
{
    /// <summary>DEBUFF-001: instância runtime de um modificador aplicado a um jogador.</summary>
    public sealed class ModifierInstance
    {
        public int InstanceId;
        public ModifierDefinition Definition;
        public int OwnerPlayerId;
        /// <summary>Jogador que causou o efeito; -1 quando veio do risco pós-ronda (DEBUFF-014).</summary>
        public int SourcePlayerId;
        public string SourceEventId;
        public int StartRound;
        public int RemainingDuration;
        public int Intensity = 1;

        public bool IsFromRisk => SourcePlayerId < 0;
    }

    /// <summary>
    /// Lista de modificadores activos de um jogador com políticas de stack, conflito e prioridade
    /// (DEBUFF-011/012/013). Ordem de aplicação é determinística: Priority, depois InstanceId.
    /// </summary>
    public sealed class ModifierController
    {
        readonly List<ModifierInstance> _active = new List<ModifierInstance>();

        public IReadOnlyList<ModifierInstance> Active => _active;

        public bool CanApply(ModifierDefinition definition, int maxGameplayModifiers, out string reason)
        {
            return Plan(definition, maxGameplayModifiers, out reason, out _, out _);
        }

        public ModifierInstance Apply(ModifierDefinition definition, int ownerId, int sourceId, string sourceEventId,
            int round, int instanceId, int maxGameplayModifiers)
        {
            if (!Plan(definition, maxGameplayModifiers, out _, out var existing, out var toRemove))
                return null;

            if (toRemove != null)
                foreach (var removed in toRemove)
                    _active.Remove(removed);

            if (existing != null)
            {
                if (definition.StackPolicy == StackPolicy.MaxIntensity && existing.Intensity < definition.MaxIntensity)
                    existing.Intensity++;
                existing.RemainingDuration = definition.DurationValue;
                existing.SourcePlayerId = sourceId;
                existing.SourceEventId = sourceEventId;
                return existing;
            }

            var instance = new ModifierInstance
            {
                InstanceId = instanceId,
                Definition = definition,
                OwnerPlayerId = ownerId,
                SourcePlayerId = sourceId,
                SourceEventId = sourceEventId,
                StartRound = round,
                RemainingDuration = definition.DurationValue,
                Intensity = 1
            };
            _active.Add(instance);
            _active.Sort(CompareByPriority);
            return instance;
        }

        /// <summary>DEBUFF-002: consome uma unidade de duração do tipo indicado e devolve os expirados.</summary>
        public List<ModifierInstance> Tick(ModifierDurationType type)
        {
            var expired = new List<ModifierInstance>();
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var m = _active[i];
                if (m.Definition.DurationType != type) continue;
                m.RemainingDuration--;
                if (m.RemainingDuration <= 0)
                {
                    expired.Add(m);
                    _active.RemoveAt(i);
                }
            }
            return expired;
        }

        public void Clear() => _active.Clear();

        bool Plan(ModifierDefinition def, int maxGameplay, out string reason, out ModifierInstance existing, out List<ModifierInstance> toRemove)
        {
            reason = null;
            toRemove = null;
            existing = null;

            foreach (var m in _active)
            {
                if (m.Definition == def)
                {
                    existing = m;
                    break;
                }
            }

            if (existing != null)
            {
                switch (def.StackPolicy)
                {
                    case StackPolicy.Reject:
                        reason = "Efeito já activo";
                        existing = null;
                        return false;
                    case StackPolicy.Refresh:
                    case StackPolicy.MaxIntensity:
                        return true;
                    case StackPolicy.Replace:
                        toRemove = new List<ModifierInstance> { existing };
                        existing = null;
                        break;
                    case StackPolicy.Stack:
                        existing = null;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(def.ConflictGroup))
            {
                foreach (var m in _active)
                {
                    if (m.Definition == def || m.Definition.ConflictGroup != def.ConflictGroup) continue;
                    if (def.StackPolicy == StackPolicy.Reject)
                    {
                        reason = "Conflito com " + m.Definition.DisplayName;
                        return false;
                    }
                    if (toRemove == null) toRemove = new List<ModifierInstance>();
                    if (!toRemove.Contains(m)) toRemove.Add(m);
                }
            }

            if (def.CountsTowardGameplayLimit && maxGameplay > 0)
            {
                int count = 0;
                foreach (var m in _active)
                    if (m.Definition.CountsTowardGameplayLimit && (toRemove == null || !toRemove.Contains(m)))
                        count++;
                if (count >= maxGameplay)
                {
                    reason = "Limite de efeitos activos";
                    return false;
                }
            }

            return true;
        }

        static int CompareByPriority(ModifierInstance a, ModifierInstance b)
        {
            int byPriority = a.Definition.Priority.CompareTo(b.Definition.Priority);
            return byPriority != 0 ? byPriority : a.InstanceId.CompareTo(b.InstanceId);
        }
    }
}
