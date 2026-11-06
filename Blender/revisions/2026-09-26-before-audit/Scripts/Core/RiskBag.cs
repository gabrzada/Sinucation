using System;

namespace FriendSlop.Core
{
    public enum RiskOutcome
    {
        Debuff,
        Discharge
    }

    /// <summary>
    /// Revólver da roleta: <see cref="TotalTokens"/> tambores, dos quais alguns carregados com bala (gosma = eliminação)
    /// e o resto com debuffs. A carga cresce exponencialmente com a ronda: balas(r) = base × crescimento^(r−1),
    /// arredondado e limitado para ficar sempre pelo menos um tambor de debuff. Cada gatilho gira o tambor.
    /// </summary>
    public sealed class RiskBag
    {
        readonly RandomService _rng;

        public RiskBag(int dangerTokens, int totalTokens, RandomService rng, float growth = 1f)
        {
            _rng = rng;
            TotalTokens = totalTokens < 1 ? 1 : totalTokens;
            DangerTokens = dangerTokens < 0 ? 0 : (dangerTokens > TotalTokens ? TotalTokens : dangerTokens);
            Growth = growth < 1f ? 1f : growth;
        }

        /// <summary>Balas na primeira ronda.</summary>
        public int DangerTokens { get; }
        /// <summary>Tambores do revólver.</summary>
        public int TotalTokens { get; }
        /// <summary>Multiplicador de balas por ronda (1 = constante).</summary>
        public float Growth { get; }

        /// <summary>Resultados seguros consecutivos desde o último disparo (informação pública).</summary>
        public int DryStreak { get; private set; }

        /// <summary>Tambores carregados na ronda indicada (1 = primeira ronda).</summary>
        public int LoadedFor(int round) => Loaded(DangerTokens, TotalTokens, Growth, round);

        public float ChanceFor(int round) => LoadedFor(round) / (float)TotalTokens;

        public static int Loaded(int baseBullets, int chambers, float growth, int round)
        {
            if (baseBullets <= 0 || chambers <= 0) return 0;
            if (baseBullets >= chambers) return chambers;
            int r = round < 1 ? 1 : round;
            double raw = baseBullets * Math.Pow(growth < 1f ? 1f : growth, r - 1);
            int loaded = (int)Math.Floor(raw + 0.5);
            if (loaded < baseBullets) loaded = baseBullets;
            if (loaded > chambers - 1) loaded = chambers - 1;
            return loaded;
        }

        public RiskOutcome Draw(int round)
        {
            int loaded = LoadedFor(round);
            int chamber = _rng.Range(0, TotalTokens);
            var token = chamber < loaded ? RiskOutcome.Discharge : RiskOutcome.Debuff;
            if (token == RiskOutcome.Discharge) DryStreak = 0;
            else DryStreak++;
            return token;
        }
    }
}
