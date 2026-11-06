using System;
using FriendSlop.Core;
using System.Globalization;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// -fsVolumeLab "url1|url2|…": volume do vídeo com a jukebox a 100 %. Para cada vídeo (32 s): painel da jukebox,
    /// a ver a TV, junto à mesa e num canto afastado. Regista a cada segundo (com a hora, para cruzar com o medidor de
    /// áudio do Windows) o volume enviado e as parcelas (geral × jukebox × distância/paredes) e o que a página diz:
    /// volume do player, volume real do &lt;video&gt; (normalização do YouTube) e loudness do vídeo.
    /// </summary>
    public sealed partial class AutoPilot
    {
        const float VolumeLabSlot = 32f;
        float _volStart = -1f, _volLogAt;
        int _volVideo = -1, _volSpot = -1;

        void UpdateVolumeLab(NetGame net)
        {
            if (!Has("-fsVolumeLab") || !net.IsServer) return;
            var player = OnlineMusicPlayer.Instance;
            var media = _game.Media;
            var local = NetPlayer.Local;
            if (player == null || media == null || local == null) return;
            string[] links = Arg("-fsVolumeLab").Split('|');
            if (_volStart < 0f)
            {
                if (_time < 6f) return;
                _volStart = _time;
                OnlineMusicPlayer.LegacyVolume = Has("-fsVolumeLegacy");
                _game.Settings.JukeboxVolume = 1f;
                _game.Settings.JukeboxMuted = false;
                player.EnablePlayer();
                Log("volume: jukebox 100 %, geral " + _game.Settings.MasterVolume.ToString("0.00", CultureInfo.InvariantCulture) + ", " + links.Length + " vídeos, curva " +
                    (OnlineMusicPlayer.LegacyVolume ? "ANTIGA" : "nova"));
            }
            // Um link a cada 9 s (o anfitrião recusa adicionar mais depressa: 8 s entre pedidos).
            int added = Mathf.Min(links.Length, 1 + Mathf.FloorToInt((_time - _volStart) / 9f));
            for (int i = 0; i < added; i++)
                if (_shots.Add("vol_add" + i)) net.AddYouTube(links[i], OnlineTrack.FromJukebox);
            float t = _time - _volStart - 10f;
            if (t < 0f) return;
            int video = Mathf.FloorToInt(t / VolumeLabSlot);
            if (video >= links.Length)
            {
                if (_shots.Add("vol_done")) Quit("laboratório de volume terminado");
                return;
            }
            if (video != _volVideo)
            {
                if (_volVideo >= 0) net.ControlOnlineMusic(OnlineMusicCommand.Next);
                _volVideo = video;
            }
            int spot = Mathf.FloorToInt((t - video * VolumeLabSlot) / (VolumeLabSlot / 4f));
            if (spot != _volSpot)
            {
                _volSpot = spot;
                _game.Interaction.Close();
                if (spot == 0)
                {
                    // Como um jogador: chega-se à jukebox (alcance de interacção) e abre o painel.
                    Vector3 box = _game.Jukebox.transform.position;
                    Vector3 at = box - _game.Jukebox.transform.forward * 1.4f; // a frente do modelo é -forward (costas na parede)
                    at.y = 0.05f;
                    float yaw = Mathf.Atan2(box.x - at.x, box.z - at.z) * Mathf.Rad2Deg;
                    local.TeleportTo(at, yaw);
                    local.SetLook(yaw, 5f);
                    _game.Interaction.DebugOpen(_game.Jukebox);
                }
                else if (spot == 1) _game.Interaction.DebugOpen(media.Tv);
                else
                {
                    Vector3 at = spot == 2 ? new Vector3(0f, 0.05f, -1.7f) : new Vector3(-6.3f, 0.05f, 5.3f);
                    local.TeleportTo(at, Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg);
                    local.SetLook(Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg, 5f);
                }
                if (Has("-fsShots")) Schedule("vol" + video + "_" + SpotName(spot), 3f, true);
            }
            if (_time < _volLogAt) return;
            _volLogAt = _time + 1f;
            var s = net.View.Music.Online;
            Log("volume " + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " vídeo=" + (s.Current != null ? s.Current.VideoId : "-") +
                " sítio=" + SpotName(spot) + " estado=" + player.PlayerState + (player.AdShowing ? " ANÚNCIO" : "") +
                " enviado=" + Mathf.RoundToInt(player.SpatialVolume * 100f) + " (geral " + _game.Settings.MasterVolume.ToString("0.00", CultureInfo.InvariantCulture) +
                " × jukebox " + _game.Settings.JukeboxVolume.ToString("0.00", CultureInfo.InvariantCulture) + " × distância " + player.LastAttenuation.ToString("0.00", CultureInfo.InvariantCulture) +
                (player.Occluded ? " abafado" : "") + " a " + player.LastDistance.ToString("0.0", CultureInfo.InvariantCulture) + " m) página: player=" + player.PageVolume +
                (player.PageMuted ? " MUDO" : "") + " video=" + player.ElementVolume.ToString("0.000", CultureInfo.InvariantCulture) + " loudness=" + player.LoudnessDb.ToString("0.0", CultureInfo.InvariantCulture) + "dB");
        }

        static string SpotName(int spot) => spot == 0 ? "jukebox" : spot == 1 ? "tv" : spot == 2 ? "mesa" : "canto";
    }
}
