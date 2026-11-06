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
        /// <summary>Título enquanto o YouTube não o deu (vídeo sem oEmbed): a página oficial informa-o ao tocar.</summary>
        public const string PlaceholderTitle = "Vídeo do YouTube";

        /// <summary>Aparelho onde o item foi escolhido (a fila é uma só: jukebox e DVD mostram e mexem na mesma).</summary>
        public const byte FromJukebox = 0, FromDvd = 1;

        public string Provider = YouTube, VideoId = "", PlaylistId = "", Title = "", Channel = "", AddedByName = "";
        /// <summary>Segundos; 0 = ainda desconhecida (o player do primeiro cliente a carregar o vídeo informa).</summary>
        public double Duration;
        public ulong AddedBy;
        /// <summary><see cref="FromJukebox"/> ou <see cref="FromDvd"/>: só informativo (a UI diz "via DVD").</summary>
        public byte Device;
        /// <summary>Só no anfitrião: vídeo pedido originalmente, quando este item é uma versão alternativa.</summary>
        [NonSerialized] public string OriginalId;

        public string Url => "https://www.youtube.com/watch?v=" + VideoId;
        public string Thumbnail => "https://i.ytimg.com/vi/" + VideoId + "/mqdefault.jpg";

        public void Write(BinaryWriter w)
        {
            w.Write(Provider ?? YouTube); w.Write(VideoId ?? ""); w.Write(Clip(Title, MaxTitle)); w.Write(Clip(Channel, MaxChannel));
            w.Write(Clip(AddedByName, MaxName)); w.Write((float)Duration); w.Write(AddedBy); w.Write(Device);
        }

        public static OnlineTrack Read(BinaryReader r) => new OnlineTrack
        {
            Provider = r.ReadString(), VideoId = r.ReadString(), Title = r.ReadString(), Channel = r.ReadString(),
            AddedByName = r.ReadString(), Duration = r.ReadSingle(), AddedBy = r.ReadUInt64(), Device = r.ReadByte()
        };

        public static string DeviceName(byte device) => device == FromDvd ? "DVD" : "jukebox";

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
        Remove = 6,
        /// <summary>Anfitrião: liga/desliga o "pular música" para os outros (YouTube e rádio).</summary>
        SkipLock = 7
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
        /// <summary>Máximo que a sala espera pelo carregamento/anúncio do anfitrião no início de uma faixa.</summary>
        public const double AdHoldLimit = 45;

        public OnlineTrack Current, Previous;
        public readonly List<OnlineTrack> Queue = new List<OnlineTrack>();
        public bool Paused, HostOnly, Locked;
        /// <summary>Só o anfitrião pode pular/voltar músicas (YouTube e rádio); os outros vêem o botão desactivado.</summary>
        public bool SkipLocked;
        /// <summary>
        /// A sala espera no início da faixa enquanto a página do anfitrião carrega ou mostra um anúncio: o relógio fica
        /// parado e todos começam a música do início, juntos, quando o anúncio acaba.
        /// </summary>
        public bool AdHold;
        public double StartedAt, Offset;
        public int Revision;

        public double Position(double now) => Math.Max(0, Offset + (Paused || AdHold ? 0 : now - StartedAt));

        /// <summary>
        /// Liga/desliga a espera pelo anúncio do anfitrião. Ao ligar nos primeiros segundos volta ao início da faixa; ao
        /// desligar o relógio continua de onde ficou. Devolve se mudou (para o anfitrião incrementar a revisão).
        /// </summary>
        public bool SetAdHold(bool hold, double now)
        {
            if (hold == AdHold) return false;
            double position = Position(now);
            if (hold) Offset = position < 5 ? 0 : position;
            else Offset = position;
            StartedAt = now;
            AdHold = hold;
            return true;
        }

        /// <summary>A faixa actual chegou ao fim (com margem para os clientes que carregaram um pouco depois).</summary>
        public bool Finished(double now)
        {
            if (Current == null || Paused || AdHold) return false;
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
            w.Write(Paused); w.Write(HostOnly); w.Write(Locked); w.Write(SkipLocked); w.Write(AdHold); w.Write(StartedAt); w.Write(Offset); w.Write(Revision);
            int count = Math.Min(Queue.Count, MaxQueue);
            w.Write((byte)count);
            for (int i = 0; i < count; i++) Queue[i].Write(w);
        }

        public void Read(BinaryReader r)
        {
            Current = r.ReadBoolean() ? OnlineTrack.Read(r) : null;
            Previous = r.ReadBoolean() ? OnlineTrack.Read(r) : null;
            Paused = r.ReadBoolean(); HostOnly = r.ReadBoolean(); Locked = r.ReadBoolean(); SkipLocked = r.ReadBoolean(); AdHold = r.ReadBoolean();
            StartedAt = r.ReadDouble(); Offset = r.ReadDouble(); Revision = r.ReadInt32();
            Queue.Clear();
            int count = r.ReadByte();
            if (count > MaxQueue) throw new InvalidDataException("Online music queue exceeds limit.");
            for (int i = 0; i < count; i++) Queue.Add(OnlineTrack.Read(r));
        }
    }

    /// <summary>
    /// Regras do player na página oficial do YouTube (youtube.com/watch), partilhadas pelo player e pelo anfitrião.
    /// A página oficial toca vídeos que a editora bloqueia em players incorporados (erros 101/150/153); o que continua
    /// impossível sem conta (idade, "confirma que não és um robô") ou indisponível chega como um dos códigos abaixo.
    /// </summary>
    public static class YouTubeWatchPage
    {
        /// <summary>Exige login no YouTube (idade ou verificação): abre-se no site oficial.</summary>
        public const int LoginRequired = 1001;
        /// <summary>Indisponível neste país/conta ou transmissão terminada.</summary>
        public const int Unplayable = 1002;
        /// <summary>Aviso de conteúdo que o próprio jogador confirma no player (não salta a faixa).</summary>
        public const int NeedsConfirmation = 1003;
        /// <summary>Removido ou privado.</summary>
        public const int Removed = 100;

        /// <summary>
        /// Navegações fora do YouTube (login da Google, anúncios, outros sites) não abrem no jogo: são canceladas e, se
        /// vierem de um clique do jogador, vão para o navegador do sistema. A escolha de cookies do YouTube fica.
        /// </summary>
        public const string HookPattern = @"^(?!https://(www\.|m\.)?youtube\.com/|https://consent\.(youtube|google)\.com/|about:blank)";

        static readonly Regex VideoId = new Regex("^[A-Za-z0-9_-]{11}$");

        /// <summary>Página oficial do vídeo, a começar na posição da sala (segundos inteiros).</summary>
        public static string Url(string videoId, double position)
        {
            if (string.IsNullOrEmpty(videoId) || !VideoId.IsMatch(videoId)) return null;
            int start = position > 1 && position < 12 * 3600 ? (int)position : 0;
            return "https://www.youtube.com/watch?v=" + videoId + (start > 0 ? "&t=" + start + "s" : "");
        }

        /// <summary>Erros que nenhum cliente consegue tocar no jogo: o anfitrião procura outra versão da mesma música.</summary>
        public static bool ShouldSearchAlternative(int code) => code == 101 || code == 150 || code == 153 || code == LoginRequired || code == Unplayable;

        /// <summary>Texto curto para o jogador (PT).</summary>
        public static string Describe(int code)
        {
            switch (code)
            {
                case LoginRequired: return "O YouTube pede login neste vídeo (idade ou verificação) — abre-o no YouTube.";
                case Unplayable: return "Vídeo indisponível aqui (país, conta ou transmissão terminada).";
                case NeedsConfirmation: return "O YouTube pede uma confirmação: clica no vídeo para continuar.";
                case Removed: return "Vídeo removido ou privado.";
                case 101: case 150: case 153: return "A editora bloqueia este vídeo fora do YouTube.";
                default: return "YouTube: erro " + code + " neste vídeo.";
            }
        }

        /// <summary>Só se abrem no navegador do sistema endereços HTTPS (nunca file:, javascript:, etc.).</summary>
        public static bool IsSafeExternal(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && url.Length <= 2048;
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
