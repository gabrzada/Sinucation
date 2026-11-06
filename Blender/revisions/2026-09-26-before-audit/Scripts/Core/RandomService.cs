using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FriendSlop.Core
{
    /// <summary>
    /// RNG autoritativo da partida (MULT-004). Ordem de turnos, punição, risco e debuffs passam
    /// por aqui; cada sorteio ponderado fica registado com eventId, elegíveis e resultado.
    /// A seed nunca é exposta aos clientes.
    /// </summary>
    public sealed class RandomService
    {
        readonly Random _random;
        readonly MatchEventLog _log;
        int _eventSequence;

        public RandomService(int seed, MatchEventLog log)
        {
            Seed = seed;
            _random = new Random(seed);
            _log = log;
        }

        public int Seed { get; }

        public string NewEventId(string prefix) => prefix + "-" + (++_eventSequence).ToString("D4");

        public int Range(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        public double NextDouble() => _random.NextDouble();

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        public T ResolveWeightedRoll<T>(string eventId, string eventType, IReadOnlyList<T> entries, Func<T, float> weight, Func<T, string> describe)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("Weighted roll needs at least one entry.", nameof(entries));

            var weights = new double[entries.Count];
            double total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                weights[i] = Math.Max(0f, weight(entries[i]));
                total += weights[i];
            }

            int selected = entries.Count - 1;
            if (total <= 0)
            {
                selected = _random.Next(0, entries.Count);
            }
            else
            {
                double roll = _random.NextDouble() * total;
                for (int i = 0; i < entries.Count; i++)
                {
                    roll -= weights[i];
                    if (roll < 0)
                    {
                        selected = i;
                        break;
                    }
                }
            }

            if (_log != null)
            {
                var sb = new StringBuilder("eligible=[");
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(describe(entries[i])).Append(':').Append(weights[i].ToString("0.##", CultureInfo.InvariantCulture));
                }
                sb.Append("] selected=").Append(describe(entries[selected]));
                _log.Record(eventType, eventId, sb.ToString());
            }

            return entries[selected];
        }
    }
}
