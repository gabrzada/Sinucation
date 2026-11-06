using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FriendSlop.Core
{
    public readonly struct MatchLogEntry
    {
        public readonly int Sequence;
        public readonly DateTime TimestampUtc;
        public readonly string Type;
        public readonly string EventId;
        public readonly string Detail;

        public MatchLogEntry(int sequence, DateTime timestampUtc, string type, string eventId, string detail)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            Type = type;
            EventId = eventId;
            Detail = detail;
        }
    }

    /// <summary>
    /// Log estruturado da partida (PUN-010, NFR-008, NFR-014). Cada evento crítico recebe
    /// um número de sequência autoritativo e pode ser exportado como JSON Lines.
    /// </summary>
    public sealed class MatchEventLog
    {
        readonly List<MatchLogEntry> _entries = new List<MatchLogEntry>();

        public MatchEventLog(string matchId, int configVersion)
        {
            MatchId = matchId;
            ConfigVersion = configVersion;
        }

        public string MatchId { get; }
        public int ConfigVersion { get; }
        public IReadOnlyList<MatchLogEntry> Entries => _entries;

        public event Action<MatchLogEntry> Recorded;

        public void Record(string type, string eventId, string detail)
        {
            var entry = new MatchLogEntry(_entries.Count + 1, DateTime.UtcNow, type, eventId ?? string.Empty, detail ?? string.Empty);
            _entries.Add(entry);
            Recorded?.Invoke(entry);
        }

        public string ToJsonLines()
        {
            var sb = new StringBuilder();
            foreach (var e in _entries)
            {
                sb.Append("{\"seq\":").Append(e.Sequence)
                  .Append(",\"t\":\"").Append(e.TimestampUtc.ToString("o", CultureInfo.InvariantCulture))
                  .Append("\",\"match\":\"").Append(Escape(MatchId))
                  .Append("\",\"cfg\":").Append(ConfigVersion)
                  .Append(",\"type\":\"").Append(Escape(e.Type))
                  .Append("\",\"eventId\":\"").Append(Escape(e.EventId))
                  .Append("\",\"detail\":\"").Append(Escape(e.Detail))
                  .Append("\"}\n");
            }
            return sb.ToString();
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
