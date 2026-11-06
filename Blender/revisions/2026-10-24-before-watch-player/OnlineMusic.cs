using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace FriendSlop.Core
{
    /// <summary>
    /// Item da fila da jukebox. Só identificadores e metadados curtos viajam na rede: URL e miniatura derivam do ID,
    /// e cada cliente reproduz no player oficial do fornecedor (nunca áudio pela rede do jogo).
    /// </summary>
    [Serializable]
    public sealed class OnlineTrack
    {
        public const string YouTube = "youtube";
        public const int MaxTitle = 80, MaxChannel = 40, MaxName = 24;

        public string Provider = YouTube, VideoId = "", PlaylistId = "", Title = "", Channel = "", AddedByName = "";
        /// <summary>Segundos; 0 = ainda desconhecida (o player do primeiro cliente a carregar o vídeo informa).</summary>
        public double Duration;
        public ulong AddedBy;
        /// <summary>Só no anfitrião: vídeo pedido originalmente, quando este item é uma versão alternativa.</summary>
        [NonSerialized] public string OriginalId;

        public string Url => "https://www.youtube.com/watch?v=" + VideoId;
        public string Thumbnail => "https://i.ytimg.com/vi/" + VideoId + "/mqdefault.jpg";

        public void Write(BinaryWriter w)
        {
            w.Write(Provider ?? YouTube); w.Write(VideoId ?? ""); w.Write(Clip(Title, MaxTitle)); w.Write(Clip(Channel, MaxChannel));
            w.Write(Clip(AddedByName, MaxName)); w.Write((float)Duration); w.Write(AddedBy);
        }

        public static OnlineTrack Read(BinaryReader r) => new OnlineTrack
        {
            Provider = r.ReadString(), VideoId = r.ReadString(), Title = r.ReadString(), Channel = r.ReadString(),
            AddedByName = r.ReadString(), Duration = r.ReadSingle(), AddedBy = r.ReadUInt64()
        };

        public static string Clip(string value, int max) => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value.Substring(0, max - 1) + "…";
    }

    public enum OnlineMusicCommand
    {
        PlayPause = 0,
        Next = 1,
        Previous = 2,
        Clear = 3,
        Lock = 4,
        HostOnly = 5,
        Remove = 6
    }

    /// <summary>
    /// Estado partilhado da jukebox online (autoritativo no anfitrião): faixa actual, fila, pausa e relógio.
    /// A posição é derivada do tempo do servidor (StartedAt/Offset), por isso quem entra a meio sincroniza sozinho.
    /// </summary>
    public sealed class OnlineMusicState
    {
        /// <summary>Limites anti-abuso. A fila inteira cabe numa mensagem de rede (~30 × 180 bytes).</summary>
        public const int MaxQueue = 30, MaxPerPlayer = 8, PlaylistBatch = 8;
        public const double AddCooldownSeconds = 8, ControlCooldownSeconds = 0.5;
        /// <summary>Faixa sem duração conhecida (ninguém com o player activo) avança ao fim deste tempo.</summary>
        public const double UnknownDurationLimit = 15 * 60;

        public OnlineTrack Current, Previous;
        public readonly List<OnlineTrack> Queue = new List<OnlineTrack>();
        public bool Paused, HostOnly, Locked;
        public double StartedAt, Offset;
        public int Revision;

        public double Position(double now) => Math.Max(0, Offset + (Paused ? 0 : now - StartedAt));

        /// <summary>A faixa actual chegou ao fim (com margem para os clientes que carregaram um pouco depois).</summary>
        public bool Finished(double now)
        {
            if (Current == null || Paused) return false;
            double limit = Current.Duration > 0 ? Current.Duration + 2 : UnknownDurationLimit;
            return Position(now) > limit;
        }

        public int CountBy(ulong sender)
        {
            int count = Current != null && Current.AddedBy == sender ? 1 : 0;
            foreach (var item in Queue) if (item.AddedBy == sender) count++;
            return count;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(Current != null); Current?.Write(w);
            w.Write(Previous != null); Previous?.Write(w);
            w.Write(Paused); w.Write(HostOnly); w.Write(Locked); w.Write(StartedAt); w.Write(Offset); w.Write(Revision);
            int count = Math.Min(Queue.Count, MaxQueue);
            w.Write((byte)count);
            for (int i = 0; i < count; i++) Queue[i].Write(w);
        }

        public void Read(BinaryReader r)
        {
            Current = r.ReadBoolean() ? OnlineTrack.Read(r) : null;
            Previous = r.ReadBoolean() ? OnlineTrack.Read(r) : null;
            Paused = r.ReadBoolean(); HostOnly = r.ReadBoolean(); Locked = r.ReadBoolean();
            StartedAt = r.ReadDouble(); Offset = r.ReadDouble(); Revision = r.ReadInt32();
            Queue.Clear();
            int count = r.ReadByte();
            if (count > MaxQueue) throw new InvalidDataException("Online music queue exceeds limit.");
            for (int i = 0; i < count; i++) Queue.Add(OnlineTrack.Read(r));
        }
    }

    public static class YouTubeLink
    {
        static readonly Regex VideoPattern = new Regex("^[A-Za-z0-9_-]{11}$");
        static readonly Regex PlaylistPattern = new Regex("^[A-Za-z0-9_-]{10,100}$");

        /// <summary>Valida um link do YouTube (só HTTPS e domínios oficiais) e extrai o vídeo e/ou a playlist.</summary>
        public static bool TryParse(string value, out string video, out string playlist)
        {
            video = playlist = "";
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512) return false;
            value = value.Trim();
            // Quem cola "youtube.com/..." ou "youtu.be/..." sem esquema também é aceite (sempre por HTTPS).
            if (!value.Contains("://")) value = "https://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
            string host = uri.IdnHost.ToLowerInvariant();
            if (host != "youtube.com" && host != "www.youtube.com" && host != "m.youtube.com" && host != "music.youtube.com" && host != "youtu.be") return false;
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                string data = Uri.UnescapeDataString(parts[1]);
                if (parts[0] == "v") video = data;
                else if (parts[0] == "list") playlist = data;
            }
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (host == "youtu.be") video = segments[0];
            else if (segments.Length == 2 && (segments[0] == "shorts" || segments[0] == "embed" || segments[0] == "live")) video = segments[1];
            if (video.Length > 0 && !VideoPattern.IsMatch(video)) return false;
            if (playlist.Length > 0 && !PlaylistPattern.IsMatch(playlist)) return false;
            return video.Length > 0 || playlist.Length > 0;
        }

        /// <summary>
        /// Link "watch?v=…&amp;list=…" (vídeo aberto dentro de uma playlist ou mix) adiciona só o vídeo, como a
        /// partilha do YouTube; a playlist inteira entra com o link "youtube.com/playlist?list=…".
        /// </summary>
        public static bool IsPlaylistRequest(string video, string playlist) => playlist.Length > 0 && video.Length == 0;
    }
}
