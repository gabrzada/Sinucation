using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// "Ver na TV": a câmara fica em frente ao ecrã e uma barra fina em baixo dá os mesmos controlos da jukebox (a fila e
    /// as permissões são as mesmas), o volume local e o atalho para escolher vídeos no DVD. Cliques no vídeo vão para a
    /// página oficial (pular anúncio quando o YouTube deixar, escolha de cookies, avisos).
    /// </summary>
    public sealed partial class HudView
    {
        void DrawTvControls(NetGame net, TvStation tv)
        {
            var music = net.View.Music;
            var online = music.Online;
            var tracks = MusicLibrary.Tracks;
            var player = OnlineMusicPlayer.Instance;
            double now = net.NetworkManager != null ? net.NetworkManager.ServerTime.Time : 0;
            bool youtube = online.Current != null;
            bool radio = !youtube && music.Track >= 0 && music.Track < tracks.Count;
            bool control = youtube ? net.CanControlOnlineMusic : net.CanControlRadio;
            JukeboxShortcuts(net, youtube, control);
            ForwardTvPointer(tv, player);

            float W = Mathf.Min(1180f, _vw - 60f);
            var bar = new Rect((_vw - W) * 0.5f, _vh - 138f, W, 118f);
            Plate(bar, NavyDark, true, 18f);
            float x = bar.x + 22f;

            // ---- o que passa
            string kicker, title, info;
            double position = 0, duration = 0;
            bool paused = false;
            if (youtube)
            {
                var current = online.Current;
                kicker = "YOUTUBE  ·  via " + OnlineTrack.DeviceName(current.Device).ToUpperInvariant();
                title = current.Title;
                info = "pedido por " + current.AddedByName + (online.Queue.Count > 0 ? "  ·  a seguir: " + online.Queue[0].Title : "");
                position = online.Position(now);
                duration = current.Duration;
                paused = online.Paused;
            }
            else if (radio)
            {
                kicker = "RÁDIO  ·  SÓ ÁUDIO";
                title = tracks[music.Track].Title;
                info = tracks[music.Track].Author + "  ·  faixa CC0 sem vídeo";
                position = music.RadioPosition(now);
                duration = tracks[music.Track].Clip != null ? tracks[music.Track].Clip.length : 0;
                paused = music.Paused;
            }
            else
            {
                kicker = "CAÇAPA TV";
                title = "Nada a passar";
                info = "Escolhe um vídeo no leitor de DVD, por baixo da TV ([E]).";
            }
            float infoW = W - 22f - 590f;
            T(new Rect(x, bar.y + 10f, infoW, 22f), kicker, 14, Mustard, TextAnchor.MiddleLeft, true);
            T(new Rect(x, bar.y + 32f, infoW, 32f), title, 22, Ivory, TextAnchor.MiddleLeft, true);
            T(new Rect(x, bar.y + 64f, infoW, 20f), info, 14, Muted, TextAnchor.MiddleLeft);
            if (youtube || radio) ProgressBar(new Rect(x, bar.y + 94f, infoW - 120f, 8f), position, duration);

            // ---- controlos (mesmas regras do painel da jukebox)
            float cx = bar.xMax - 590f, by = bar.y + 16f;
            bool has = youtube || radio;
            bool skipOk = net.CanSkipMusic;
            if (Secondary(new Rect(cx, by, 110f, 52f), "ANTERIOR", control && has && skipOk && (!youtube || online.Previous != null), 16))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.Previous); else net.ControlRadio(2);
            }
            if (Primary(new Rect(cx + 118f, by, 130f, 52f), paused ? "PLAY" : "PAUSA", control && has, 20, paused ? IconKind.Play : IconKind.None))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.PlayPause); else net.ControlRadio(0);
            }
            if (Secondary(new Rect(cx + 256f, by, 110f, 52f), youtube || net.IsServer ? "PRÓXIMA" : "PRÓX. " + music.SkipVotes + "/" + Mathf.Max(1, music.SkipNeeded), (youtube ? control : true) && has && skipOk, 16))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.Next); else net.ControlRadio(1);
            }
            var settings = _game.Settings;
            if (Btn(new Rect(cx + 374f, by, 96f, 52f), settings.JukeboxMuted ? "MUDO" : Mathf.RoundToInt(settings.JukeboxVolume * 100f) + "%", settings.JukeboxMuted ? Coral : Navy, Ivory, true, 16))
            {
                settings.JukeboxMuted = !settings.JukeboxMuted;
                settings.Save();
            }
            // Sem player activo: o mesmo consentimento da jukebox; se falhou, a alternativa no site oficial.
            if (youtube && player != null && !player.Enabled)
            {
                if (player.Failed) { if (Secondary(new Rect(cx + 478f, by, 100f, 52f), "YOUTUBE", true, 15)) player.OpenOnYouTube(); }
                else if (Primary(new Rect(cx + 478f, by, 100f, 52f), "[J] VER", true, 16)) player.EnablePlayer();
            }
            else if (youtube && player != null && player.AutoplayBlocked)
            {
                if (Primary(new Rect(cx + 478f, by, 100f, 52f), "[J] SOM", true, 16)) player.RetryPlayback();
            }
            string hint = !control ? "Só o anfitrião controla (modo \"só anfitrião\")." : !skipOk ? "Pular bloqueado pelo anfitrião." :
                youtube && player != null && player.Enabled && player.StatusIsProblem ? player.Status : "ESPAÇO pausa · ← → · ↑ ↓ volume · M mudo · Q sair";
            T(new Rect(cx, bar.y + 76f, 570f, 32f), hint, 13, !control || !skipOk || (player != null && player.StatusIsProblem) ? Coral : Muted, TextAnchor.MiddleLeft, false, false, true);
        }

        /// <summary>Cliques e roda do rato em cima do vídeo da TV vão para a mesma posição da página oficial.</summary>
        void ForwardTvPointer(TvStation tv, OnlineMusicPlayer player)
        {
            if (player == null || !player.Enabled) return;
            var e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseUp && e.type != EventType.ScrollWheel) return;
            var screen = new Vector2(e.mousePosition.x * _scale, Screen.height - e.mousePosition.y * _scale);
            if (!tv.ScreenToVideo(screen, out var n)) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0: player.Pointer(n, 1); e.Use(); break;
                case EventType.MouseUp when e.button == 0: player.Pointer(n, 3); e.Use(); break;
                case EventType.ScrollWheel when e.delta.y != 0f: player.Pointer(n, 0, -Mathf.Sign(e.delta.y)); e.Use(); break;
            }
        }
    }
}
