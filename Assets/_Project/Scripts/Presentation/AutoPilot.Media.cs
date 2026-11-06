using System.Collections.Generic;
using System.Text;
using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    public sealed partial class AutoPilot
    {
        // -fsMediaTest host|client|late: validação da TV + DVD + jukebox com vários processos (anfitrião, cliente e
        // um cliente que entra a meio). Cada processo regista o estado da sala, o que a TV mostra e o desvio entre a
        // página do YouTube e o relógio da sala; -fsShots guarda capturas da TV e dos painéis.
        // -fsMediaA / -fsMediaB: links a usar (por omissão dois filmes abertos da Blender Foundation).
        float _mediaStart = -1f, _mediaLogAt;
        readonly HashSet<string> _mediaDone = new HashSet<string>();

        bool MediaStep(string name, float at) => _time - _mediaStart >= at && _mediaDone.Add(name);

        void UpdateMediaTest(NetGame net)
        {
            if (!Has("-fsMediaTest")) return;
            string role = Arg("-fsMediaTest");
            var media = _game.Media;
            var player = OnlineMusicPlayer.Instance;
            if (media == null || player == null) return;
            if (_mediaStart < 0f)
            {
                _mediaStart = _time;
                net.MusicNotice += text => Log("media aviso: " + text);
                net.Toast += text => Log("media toast: " + text);
                _game.Settings.JukeboxVolume = Has("-fsJukeboxVolume") && float.TryParse(Arg("-fsJukeboxVolume"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v) : 0.03f;
                _game.Settings.JukeboxMuted = false;
                Log("media: papel=" + role + " anfitrião=" + net.IsServer);
            }
            string a = Has("-fsMediaA") ? Arg("-fsMediaA") : "https://www.youtube.com/watch?v=aqz-KE-bpKQ";
            string b = Has("-fsMediaB") ? Arg("-fsMediaB") : "https://www.youtube.com/watch?v=eRsGyueVLvQ";

            // Todos: consentimento (como carregar em [J]) e ficar a ver a TV de perto.
            if (MediaStep("enable", 2f)) { Log("media: [J] ativar player"); player.EnablePlayer(); }
            if (MediaStep("watch", 4f)) Open(media.Tv, "TV");

            if (role == "host")
            {
                if (MediaStep("add_jukebox", 6f)) { Open(_game.Jukebox, "jukebox"); Log("media: anfitrião põe A na jukebox"); net.AddYouTube(a, OnlineTrack.FromJukebox); }
                if (MediaStep("back_tv", 9f)) Open(media.Tv, "TV");
                if (MediaStep("resume_jukebox", 64f)) { Open(_game.Jukebox, "jukebox"); Log("media: anfitrião retoma na jukebox"); net.ControlOnlineMusic(OnlineMusicCommand.PlayPause); Shot("host_jukebox_panel", 1.5f); }
                if (MediaStep("back_tv2", 68f)) Open(media.Tv, "TV");
                if (MediaStep("prev_tv", 96f)) { Log("media: anfitrião ANTERIOR na TV"); net.ControlOnlineMusic(OnlineMusicCommand.Previous); }
                if (MediaStep("radio", 150f)) { Log("media: anfitrião limpa a fila e salta → rádio"); net.ControlOnlineMusic(OnlineMusicCommand.Clear); }
                if (MediaStep("radio_next", 151f)) net.ControlOnlineMusic(OnlineMusicCommand.Next);
                if (MediaStep("shot_radio", 158f)) Shot("host_tv_radio", 0.5f);
            }
            else if (role == "client")
            {
                if (MediaStep("add_dvd", 14f)) { Open(media.Dvd, "DVD"); Log("media: cliente põe B no DVD"); net.AddYouTube(b, OnlineTrack.FromDvd); Shot("client_dvd_panel", 2f); }
                if (MediaStep("back_tv", 18f)) Open(media.Tv, "TV");
                if (MediaStep("pause_tv", 54f)) { Log("media: cliente PAUSA na TV"); net.ControlOnlineMusic(OnlineMusicCommand.PlayPause); Shot("client_tv_paused", 2.5f); }
                if (MediaStep("next_dvd", 80f)) { Open(media.Dvd, "DVD"); Log("media: cliente PRÓXIMA no DVD"); net.ControlOnlineMusic(OnlineMusicCommand.Next); Shot("client_dvd_after_next", 2f); }
                if (MediaStep("back_tv2", 84f)) Open(media.Tv, "TV");
                if (MediaStep("fail", 110f)) { Log("media: cliente simula falha do player (WebView fechado)"); player.SimulateFailure("Simulação de teste: o navegador embutido fechou."); Shot("client_tv_failed", 2f); }
                if (MediaStep("recover", 122f)) { Log("media: cliente volta a ativar o player"); player.EnablePlayer(); }
                if (MediaStep("bad_link", 126f)) { Log("media: cliente põe um vídeo inexistente no DVD"); net.AddYouTube("https://youtu.be/zzzzzzzzzz0", OnlineTrack.FromDvd); }
                if (MediaStep("shot_recovered", 140f)) Shot("client_tv_recovered", 0.5f);
                if (MediaStep("shot_radio", 160f)) Shot("client_tv_radio", 0.5f);
            }
            // Capturas da TV a tocar em cada processo (o "late" entra a meio e mostra a mesma posição).
            if (MediaStep("shot_video", role == "late" ? 30f : 44f)) Shot(role + "_tv_video", 0.5f);
            if (role == "late" && MediaStep("shot_video2", 60f)) Shot("late_tv_video2", 0.5f);

            if (_time >= _mediaLogAt)
            {
                _mediaLogAt = _time + 3f;
                var s = net.View.Music.Online;
                double now = net.NetworkManager.ServerTime.Time;
                double room = s.Position(now);
                var sb = new StringBuilder("media[").Append(role).Append("] t=").Append((_time - _mediaStart).ToString("0"));
                if (s.Current != null)
                    sb.Append(" atual=").Append(s.Current.VideoId).Append(" via ").Append(OnlineTrack.DeviceName(s.Current.Device))
                      .Append(" sala=").Append(room.ToString("0.0")).Append(s.Paused ? " PAUSA" : "").Append(s.AdHold ? " ESPERA-ANÚNCIOS" : "");
                else sb.Append(" atual=- rádio=").Append(net.View.Music.Track).Append(net.View.Music.Paused ? " PAUSA" : "");
                sb.Append(" fila=[");
                for (int i = 0; i < s.Queue.Count; i++) sb.Append(i > 0 ? "," : "").Append(s.Queue[i].VideoId).Append('/').Append(OnlineTrack.DeviceName(s.Queue[i].Device));
                sb.Append("] rev=").Append(s.Revision).Append(" tv=").Append(media.Tv.Showing);
                sb.Append(" player=").Append(player.Enabled ? "on" : player.Failed ? "falhou" : "off").Append(" estado=").Append(player.PlayerState)
                  .Append(player.AdShowing ? " ANÚNCIO" : "").Append(player.WaitingForOthers ? " À-ESPERA" : "");
                if (s.Current != null && player.Enabled && player.PlayerState == 1 && !player.AdShowing)
                    sb.Append(" página=").Append(player.PlayerTime.ToString("0.0")).Append(" desvio=").Append((player.PlayerTime - room).ToString("+0.00;-0.00"));
                if (player.StatusIsProblem) sb.Append(" status=\"").Append(player.Status).Append('"');
                Log(sb.ToString());
            }
        }

        void Open(Interactable it, string label)
        {
            if (it == null) return;
            _game.Interaction.Close();
            _game.Interaction.DebugOpen(it);
            if (it is JukeboxStation || it is DvdStation) _game.Hud.ShowOnlineMusicTab();
            Log("media: aberto " + label);
        }

        void Shot(string name, float delay) => Schedule(name, delay, true);
    }
}
