using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;

namespace FriendSlop.Match
{
    /// <summary>Métricas mínimas de balanceamento (NFR-009). Tudo local à partida.</summary>
    public sealed class MatchTelemetry
    {
        public int Shots;
        public int Fouls;
        public int Timeouts;
        public int Reracks;
        /// <summary>Tacadas efectuadas sob 0 / 1 / 2+ modificadores.</summary>
        public readonly int[] ShotsByModifierCount = new int[3];
        /// <summary>Por definição: tacadas sob o efeito e quantas encaçaparam legalmente.</summary>
        public readonly Dictionary<string, int[]> ShotsUnderModifier = new Dictionary<string, int[]>();
        public readonly Dictionary<string, int> ModifierApplications = new Dictionary<string, int>();
        public int RiskEvents;
        public int Discharges;
        public readonly List<int> DryBeforeDischarge = new List<int>();
        public int SabotageAttempts;
        public int SabotageSuccesses;
        public readonly List<float> RoundDurations = new List<float>();
        public float MatchDuration;

        public void RecordShot(ShotRecord shot, RuleOutcome outcome)
        {
            Shots++;
            if (outcome.IsFoul) Fouls++;
            int bucket = shot.ActiveModifierIds.Count >= 2 ? 2 : shot.ActiveModifierIds.Count;
            ShotsByModifierCount[bucket]++;
            foreach (var id in shot.ActiveModifierIds)
            {
                if (!ShotsUnderModifier.TryGetValue(id, out var counts))
                    ShotsUnderModifier[id] = counts = new int[2];
                counts[0]++;
                if (!outcome.IsFoul && outcome.LegalPockets > 0) counts[1]++;
            }
        }

        public void RecordModifier(string definitionId)
        {
            ModifierApplications.TryGetValue(definitionId, out int count);
            ModifierApplications[definitionId] = count + 1;
        }

        public void RecordRisk(RiskOutcome outcome, int dryBefore)
        {
            RiskEvents++;
            if (outcome == RiskOutcome.Discharge)
            {
                Discharges++;
                DryBeforeDischarge.Add(dryBefore);
            }
        }

        public float ShareOfShots(int bucket) => Shots == 0 ? 0f : ShotsByModifierCount[bucket] / (float)Shots;
    }
}
