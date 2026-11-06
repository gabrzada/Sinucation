using System.Collections;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Painel da jukebox: um só fluxo para as duas fontes (RÁDIO com as faixas CC0 e YOUTUBE com os pedidos dos
    /// jogadores). Mesma ordem nas duas — separador da fonte (com "NO AR"), a tocar em destaque, controlos grandes,
    /// volume local, fila e pedidos — para quem aprende uma saber usar a outra. Estado sempre visível (A TOCAR /
    /// EM PAUSA / bloqueado e porquê), atalhos de teclado e controlos do anfitrião recolhidos até serem precisos.
    /// </summary>
    public sealed partial class HudView
    {
        bool _onlineMusicTab;
        string _youtubeUrl = "";
        Vector2 _onlineMusicScroll, _radioScroll;
        int _jukeboxFrame = -10;
        bool _jukeboxHostOpen;
        /// <summary>O painel foi aberto no leitor de DVD (mesma fila; o que se põe aqui fica marcado "via DVD").</summary>
        bool _dvdPanel;

        // Miniaturas (i.ytimg.com), com cache pequena: só se descarregam as que o painel mostra.
        readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _thumbsLoading = new HashSet<string>();
        readonly Queue<string> _thumbOrder = new Queue<string>();

        /// <summary>Abre o separador YouTube da jukebox (testes automáticos).</summary>
        public void ShowOnlineMusicTab() => _onlineMusicTab = true;

        Texture2D Thumbnail(OnlineTrack track)
        {
            if (track == null || string.IsNullOrEmpty(track.VideoId)) return null;
            if (_thumbs.TryGetValue(track.VideoId, out var tex)) return tex;
            if (_thumbsLoading.Add(track.VideoId)) StartCoroutine(LoadThumbnail(track.VideoId, track.Thumbnail));
            return null;
        }

        IEnumerator LoadThumbnail(string id, string url)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                _thumbsLoading.Remove(id);
                if (request.result != UnityWebRequest.Result.Success) { _thumbs[id] = null; yield break; }
                var tex = DownloadHandlerTexture.GetContent(request);
                if (tex == null) yield break;
                tex.hideFlags = HideFlags.DontSave;
                _thumbs[id] = tex;
                _thumbOrder.Enqueue(id);
                while (_thumbOrder.Count > 24)
                {
                    string old = _thumbOrder.Dequeue();
                    if (_thumbs.TryGetValue(old, out var t) && t != null) Destroy(t);
                    _thumbs.Remove(old);
                }
            }
        }

        void DrawThumb(Rect r, OnlineTrack track)
        {
            Round(r, Navy, 8f);
            var tex = Thumbnail(track);
            if (tex != null) GUI.DrawTexture(Inset(r, 3f), tex, ScaleMode.ScaleAndCrop);
            else Icon(new Rect(r.center.x - 16f, r.center.y - 16f, 32f, 32f), IconKind.Music, Muted);
        }

        // ================================================================== painel

        void DrawJukebox(NetGame net)
        {
            var music = net.View.Music;
            var online = music.Online;
            var tracks = MusicLibrary.Tracks;
            double now = net.NetworkManager != null ? net.NetworkManager.ServerTime.Time : 0;
            bool youtubeLive = online.Current != null;
            bool radioLive = !youtubeLive && music.Track >= 0 && music.Track < tracks.Count;
            _dvdPanel = _game.Interaction.Active is DvdStation;
            // Abre sempre na fonte que está a tocar (é essa que o jogador quer controlar); o DVD abre sempre nos vídeos.
            if (Time.frameCount - _jukeboxFrame > 2) _onlineMusicTab = youtubeLive || _dvdPanel;
            _jukeboxFrame = Time.frameCount;

            bool youtube = _onlineMusicTab;
            bool control = youtube ? net.CanControlOnlineMusic : net.CanControlRadio;
            JukeboxShortcuts(net, youtube, control);

            float W = Mathf.Min(840f, _vw - 60f), H = _vh - 60f;
            var panel = new Rect(_vw - W - 30f, 30f, W, H);
            Plate(panel, NavyDark, true, 20f);
            float x0 = panel.x + 24f, cw = W - 48f, y = panel.y + 18f;

            // ---- cabeçalho: título + fonte (separadores com "NO AR")
            Icon(new Rect(x0, y + 10f, 34f, 34f), IconKind.Music, Mustard);
            T(new Rect(x0 + 46f, y, 250f, 54f), _dvdPanel ? "DVD" : "JUKEBOX", 38, Mustard, TextAnchor.MiddleLeft, true);
            if (_dvdPanel) T(new Rect(x0 + 128f, y + 6f, 200f, 44f), "a mesma fila da jukebox,\npassa na TV para todos", 14, Muted, TextAnchor.MiddleLeft);
            float tabW = Mathf.Min(210f, (cw - 300f) * 0.5f);
            if (SourceTab(new Rect(panel.xMax - 24f - tabW * 2f - 10f, y + 4f, tabW, 48f), "RÁDIO", !youtube, radioLive)) _onlineMusicTab = false;
            if (SourceTab(new Rect(panel.xMax - 24f - tabW, y + 4f, tabW, 48f), "YOUTUBE", youtube, youtubeLive)) _onlineMusicTab = true;
            y += 70f;

            // ---- a tocar (destaque) + estado
            y = youtube ? DrawYouTubeHero(net, x0, y, cw, now) : DrawRadioHero(net, x0, y, cw, now, youtubeLive);

            // ---- controlos (grandes, sempre no mesmo sítio)
            float bw = (cw - 20f) / 3f;
            bool hasTrack = youtube ? online.Current != null : radioLive;
            bool paused = youtube ? online.Paused : music.Paused;
            bool prevOk = !youtube || online.Previous != null;
            // Pular bloqueado pelo anfitrião: os outros vêem ANTERIOR/PRÓXIMA desactivados e o porquê.
            bool skipOk = net.CanSkipMusic;
            if (Secondary(new Rect(x0, y, bw, 60f), "ANTERIOR", control && hasTrack && prevOk && skipOk, 20))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.Previous); else net.ControlRadio(2);
            }
            if (Primary(new Rect(x0 + bw + 10f, y, bw, 60f), paused ? "PLAY" : "PAUSA", control && hasTrack, 24, paused ? IconKind.Play : IconKind.None))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.PlayPause); else net.ControlRadio(0);
            }
            string nextLabel = "PRÓXIMA";
            // Na rádio, quem não é anfitrião vota para saltar (metade da sala).
            if (!youtube && !net.IsServer) nextLabel = "PRÓXIMA  " + music.SkipVotes + "/" + Mathf.Max(1, music.SkipNeeded);
            bool nextOk = (youtube ? control && hasTrack : hasTrack) && skipOk;
            if (Secondary(new Rect(x0 + (bw + 10f) * 2f, y, bw, 60f), nextLabel, nextOk, 20))
            {
                if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.Next); else net.ControlRadio(1);
            }
            y += 66f;
            if (!control)
            {
                T(new Rect(x0, y, cw, 24f), "Só o anfitrião controla a música (modo \"só anfitrião\").", 16, Coral, TextAnchor.MiddleLeft, true);
                y += 26f;
            }
            else if (!skipOk)
            {
                T(new Rect(x0, y, cw, 24f), "Pular música bloqueado pelo anfitrião — só ele passa à seguinte.", 16, Coral, TextAnchor.MiddleLeft, true);
                y += 26f;
            }

            // ---- volume local (vale para as duas fontes; não muda o que os outros ouvem)
            y = DrawJukeboxVolume(x0, y + 4f, cw, youtube);

            // ---- fila e pedidos (scroll), rodapé com atalhos
            float footer = 30f;
            var area = new Rect(x0, y + 6f, cw, panel.yMax - footer - 14f - (y + 6f));
            if (youtube) DrawYouTubeQueue(net, area);
            else DrawRadioQueue(net, area);
            T(new Rect(x0, panel.yMax - footer - 8f, cw, footer), "ESPAÇO pausa  ·  ← →  anterior / próxima  ·  ↑ ↓  volume  ·  M  mudo  ·  Q  fechar", 15, Muted, TextAnchor.MiddleCenter);
        }

        bool SourceTab(Rect r, string label, bool selected, bool live)
        {
            bool clicked = Btn(r, label, selected ? Mustard : Navy, selected ? NavyDark : Ivory, true, 20);
            if (live)
            {
                // "No ar": ponto verde a pulsar no canto do separador.
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
                var dot = new Rect(r.xMax - 22f, r.y + 8f, 12f, 12f);
                Round(dot, new Color(GoodGreen.r, GoodGreen.g, GoodGreen.b, pulse), 6f);
            }
            return clicked;
        }

        void JukeboxShortcuts(NetGame net, bool youtube, bool control)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUI.GetNameOfFocusedControl() == "YouTubeUrl") return;
            var settings = _game.Settings;
            switch (e.keyCode)
            {
                case KeyCode.Space:
                    if (control) { if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.PlayPause); else net.ControlRadio(0); }
                    break;
                case KeyCode.RightArrow:
                    if (!net.CanSkipMusic) break;
                    if (youtube) { if (control) net.ControlOnlineMusic(OnlineMusicCommand.Next); }
                    else net.ControlRadio(1);
                    break;
                case KeyCode.LeftArrow:
                    if (control && net.CanSkipMusic) { if (youtube) net.ControlOnlineMusic(OnlineMusicCommand.Previous); else net.ControlRadio(2); }
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                    settings.JukeboxVolume = Mathf.Clamp01(settings.JukeboxVolume + (e.keyCode == KeyCode.UpArrow ? 0.05f : -0.05f));
                    settings.JukeboxMuted = false;
                    settings.Save();
                    break;
                case KeyCode.M:
                    settings.JukeboxMuted = !settings.JukeboxMuted;
                    settings.Save();
                    break;
                default:
                    return;
            }
            e.Use();
        }

        float DrawJukeboxVolume(float x0, float y, float cw, bool youtube)
        {
            var settings = _game.Settings;
            var row = new Rect(x0, y, cw, 50f);
            Round(row, new Color(1f, 1f, 1f, 0.04f), 10f);
            if (Btn(new Rect(row.x + 8f, row.y + 6f, 104f, 38f), settings.JukeboxMuted ? "MUDO" : "SOM", settings.JukeboxMuted ? Coral : Navy, Ivory, true, 17))
            {
                settings.JukeboxMuted = !settings.JukeboxMuted;
                settings.Save();
            }
            T(new Rect(row.x + 124f, row.y, 150f, row.height), "VOLUME · SÓ TU", 15, Muted, TextAnchor.MiddleLeft, true);
            float v = Slider(new Rect(row.x + 280f, row.y + 6f, row.width - 280f - 80f, 38f), settings.JukeboxVolume, 0f, 1f);
            if (!Mathf.Approximately(v, settings.JukeboxVolume))
            {
                settings.JukeboxVolume = v;
                settings.JukeboxMuted = false;
                settings.Save();
            }
            T(new Rect(row.xMax - 74f, row.y, 64f, row.height), settings.JukeboxMuted ? "—" : Mathf.RoundToInt(settings.JukeboxVolume * 100f) + "%", 18, Mustard, TextAnchor.MiddleRight, true);
            y += 56f;
            var player = OnlineMusicPlayer.Instance;
            if (youtube && player != null && player.Enabled && NetGame.Instance.View.Music.Online.Current != null)
            {
                T(new Rect(x0 + 4f, y, cw, 20f), "Aqui ouves a " + Mathf.RoundToInt(player.SpatialVolume * 100f) + "%" + (player.Occluded ? " (paredes pelo meio)" : "") + " — mais perto da jukebox ou da TV, mais alto.", 14, Muted, TextAnchor.MiddleLeft);
                y += 22f;
            }
            return y;
        }

        // ------------------------------------------------------------------ rádio (faixas CC0)

        float DrawRadioHero(NetGame net, float x0, float y, float cw, double now, bool youtubeLive)
        {
            var music = net.View.Music;
            var tracks = MusicLibrary.Tracks;
            var hero = new Rect(x0, y, cw, 176f);
            Round(hero, Navy, 16f);
            // Disco a girar (parado em pausa).
            var disc = new Rect(hero.x + 18f, hero.y + 18f, 140f, 140f);
            Round(disc, new Color(0.04f, 0.05f, 0.07f), 70f);
            for (int i = 1; i <= 3; i++) Outline(Inset(disc, i * 14f), new Color(1f, 1f, 1f, 0.06f), 2f, 70f - i * 14f);
            Round(Inset(disc, 46f), Mustard, 24f);
            Round(Inset(disc, 64f), NavyDark, 6f);
            float spin = music.Paused || youtubeLive ? 0f : Time.unscaledTime * 90f;
            var tick = new Vector2(disc.center.x + Mathf.Cos(spin * Mathf.Deg2Rad) * 40f, disc.center.y + Mathf.Sin(spin * Mathf.Deg2Rad) * 40f);
            Round(new Rect(tick.x - 5f, tick.y - 5f, 10f, 10f), new Color(1f, 1f, 1f, 0.35f), 5f);

            float tx = disc.xMax + 22f, tw = hero.xMax - 20f - tx;
            if (youtubeLive)
            {
                T(new Rect(tx, hero.y + 22f, tw, 26f), "EM ESPERA", 16, Muted, TextAnchor.MiddleLeft, true);
                T(new Rect(tx, hero.y + 48f, tw, 70f), "A rádio volta quando a fila do YouTube acabar.", 22, Ivory, TextAnchor.UpperLeft, true, false, true);
                if (Secondary(new Rect(tx, hero.yMax - 56f, 220f, 42f), "VER YOUTUBE", true, 17)) _onlineMusicTab = true;
                return hero.yMax + 12f;
            }
            if (music.Track >= 0 && music.Track < tracks.Count)
            {
                var t = tracks[music.Track];
                StatusChip(new Rect(tx, hero.y + 20f, 130f, 26f), music.Paused);
                T(new Rect(tx, hero.y + 48f, tw, 44f), t.Title, 28, Ivory, TextAnchor.MiddleLeft, true);
                T(new Rect(tx, hero.y + 90f, tw, 24f), t.Author + "  ·  rádio grátis (CC0, OpenGameArt)", 16, Muted, TextAnchor.MiddleLeft);
                double pos = music.RadioPosition(now);
                float length = t.Clip != null ? t.Clip.length : 1f;
                ProgressBar(new Rect(tx, hero.yMax - 40f, tw - 120f, 8f), pos, length);
            }
            else T(new Rect(tx, hero.y, tw, hero.height), "Silêncio… pede uma música abaixo.", 22, Muted, TextAnchor.MiddleLeft, true);
            return hero.yMax + 12f;
        }

        void DrawRadioQueue(NetGame net, Rect area)
        {
            var music = net.View.Music;
            var tracks = MusicLibrary.Tracks;
            var content = new Rect(0f, 0f, area.width - 24f, 110f + music.Queue.Count * 44f + tracks.Count * 64f);
            _radioScroll = GUI.BeginScrollView(area, _radioScroll, content);
            float w = content.width, y = 0f;
            T(new Rect(0f, y, w, 30f), "A SEGUIR · " + music.Queue.Count, 20, Mustard, TextAnchor.MiddleLeft, true);
            y += 34f;
            if (music.Queue.Count == 0)
            {
                T(new Rect(4f, y, w, 26f), "Fila vazia — a rádio escolhe a próxima.", 16, Muted, TextAnchor.MiddleLeft);
                y += 30f;
            }
            for (int i = 0; i < music.Queue.Count; i++)
            {
                int track = music.Queue[i];
                string who = i < music.QueuedBy.Count ? WhoQueued(net, music.QueuedBy[i]) : "";
                string title = track >= 0 && track < tracks.Count ? tracks[track].Title : "?";
                T(new Rect(4f, y, w, 40f), (i + 1) + ". " + title + Colored("  ·  pedido por " + who, Muted), 18, Ivory, TextAnchor.MiddleLeft);
                y += 44f;
            }
            y += 10f;
            T(new Rect(0f, y, w, 30f), "PEDIR MÚSICA", 20, Mustard, TextAnchor.MiddleLeft, true);
            y += 36f;
            bool full = music.Queue.Count >= 8;
            for (int i = 0; i < tracks.Count; i++)
            {
                var row = new Rect(0f, y, w, 56f);
                bool playing = i == music.Track && music.Online.Current == null;
                Round(row, playing ? Color.Lerp(Navy, Mustard, 0.25f) : Navy, 12f);
                T(new Rect(row.x + 16f, row.y, row.width - 200f, row.height), tracks[i].Title + Colored("  " + tracks[i].Author, Muted) + (playing ? Colored("  · A TOCAR", GoodGreen) : ""), 19, Ivory, TextAnchor.MiddleLeft, true);
                if (Primary(new Rect(row.xMax - 150f, row.y + 7f, 140f, 42f), full ? "FILA CHEIA" : "+ FILA", !full, 18)) net.QueueTrack(i);
                y += 64f;
            }
            GUI.EndScrollView();
        }

        static string WhoQueued(NetGame net, ulong clientId)
        {
            var slot = net.View.SlotForClient(clientId);
            return slot != null ? slot.Profile.Name : "anfitrião";
        }

        // ------------------------------------------------------------------ YouTube (pedidos dos jogadores)

        float DrawYouTubeHero(NetGame net, float x0, float y, float cw, double now)
        {
            var state = net.View.Music.Online;
            var player = OnlineMusicPlayer.Instance;
            var current = state.Current;
            // O vídeo ocupa a largura toda do painel (16:9), com pelo menos 200 px de altura no ecrã.
            float vh = Mathf.Max(cw * 9f / 16f, 204f / _scale);
            vh = Mathf.Min(vh, _vh * 0.42f);
            var video = new Rect(x0, y, cw, vh);
            Round(new Rect(video.x - 3f, video.y - 3f, video.width + 6f, video.height + 6f), Navy, 12f);
            bool showVideo = player != null && player.Enabled && current != null;
            if (showVideo)
            {
                if (Event.current.type == EventType.Repaint)
                    player.ShowIn(new Rect(video.x * _scale, video.y * _scale, video.width * _scale, video.height * _scale));
                // "Máscara": só a zona do player oficial da página (ou a página inteira, se o YouTube pedir uma escolha),
                // com a proporção certa e barras escuras em vez de cortar o player.
                if (player.TryGetFrame(out var tex, out var uv, out float aspect))
                {
                    Round(video, new Color(0.04f, 0.05f, 0.07f), 10f);
                    var frame = OnlineMusicPlayer.Fit(video, aspect);
                    GUI.DrawTextureWithTexCoords(frame, tex, uv);
                    ForwardPointer(player, frame);
                    if (player.ShowsWholePage)
                        T(new Rect(video.x, video.yMax - 30f, video.width, 26f), "O YouTube pede uma escolha — responde aqui com o rato.", 15, Mustard, TextAnchor.MiddleCenter, true);
                }
                else
                {
                    DrawThumb(video, current);
                    Fill(video, new Color(0f, 0f, 0f, 0.45f));
                    T(Inset(video, 14f), player.StatusIsProblem ? player.Status : "A abrir a página oficial do YouTube…", 20, player.StatusIsProblem ? Coral : Ivory, TextAnchor.MiddleCenter, true, false, true);
                }
            }
            else if (current != null)
            {
                DrawThumb(video, current);
                if (player != null && !player.Enabled)
                {
                    // Chamada à acção clara: sem isto não se ouve nada. Sem WebView2 (ou fora do Windows) fica sempre a
                    // alternativa de ouvir no site oficial.
                    Fill(video, new Color(0f, 0f, 0f, 0.55f));
                    T(new Rect(video.x, video.center.y - 70f, video.width, 40f), player.Failed ? player.Status : "Para ouvir, ativa o player oficial do YouTube", 20, Ivory, TextAnchor.MiddleCenter, true, false, true);
                    float bw = player.Failed ? 300f : 340f;
                    float bx = player.Failed ? video.center.x - bw - 8f : video.center.x - bw * 0.5f;
                    if (Primary(new Rect(bx, video.center.y - 20f, bw, 56f), player.Failed ? "TENTAR OUTRA VEZ" : "ATIVAR PLAYER", true, 22, IconKind.Play))
                        player.EnablePlayer();
                    if (player.Failed && Secondary(new Rect(video.center.x + 8f, video.center.y - 20f, bw, 56f), "ABRIR NO YOUTUBE", true, 20))
                        player.OpenOnYouTube();
                }
            }
            else
            {
                Round(video, new Color(0.04f, 0.05f, 0.07f), 10f);
                T(new Rect(video.x + 30f, video.y, video.width - 60f, video.height), "Nada a tocar do YouTube.\nCola um link abaixo — vídeo ou playlist.", 22, Muted, TextAnchor.MiddleCenter, true, false, true);
            }
            y = video.yMax + 10f;
            if (current != null)
            {
                bool ad = player != null && player.Enabled && player.AdShowing;
                bool waiting = player != null && player.Enabled && player.WaitingForOthers;
                if (ad || waiting)
                {
                    Round(new Rect(x0, y + 2f, 130f, 26f), Darker(Mustard, 0.75f), 13f);
                    T(new Rect(x0, y + 2f, 130f, 26f), ad ? "ANÚNCIO" : "A ESPERAR", 15, NavyDark, TextAnchor.MiddleCenter, true);
                }
                else StatusChip(new Rect(x0, y + 2f, 130f, 26f), state.Paused);
                T(new Rect(x0 + 142f, y, cw - 142f - 196f, 30f), current.Title, 22, Ivory, TextAnchor.MiddleLeft, true);
                // Alternativa sempre à mão: o mesmo vídeo no site oficial, no navegador do sistema, na posição da sala.
                if (Secondary(new Rect(x0 + cw - 186f, y - 2f, 186f, 34f), "ABRIR NO YOUTUBE", true, 14)) player?.OpenOnYouTube();
                T(new Rect(x0, y + 32f, cw, 22f), (string.IsNullOrEmpty(current.Channel) ? "" : current.Channel + "  ·  ") + "pedido por " + current.AddedByName + " via " + OnlineTrack.DeviceName(current.Device) +
                    (ad ? Colored("  ·  [J] " + (player.AdMuted ? "som" : "silenciar") + "  ·  [K] " + (player.CanSkipAd ? "pular" : "pular (quando o YouTube deixar)"), Mustard)
                        : waiting ? Colored("  ·  à espera dos anúncios dos outros, começa para todos ao mesmo tempo", Mustard) : ""), 15, Muted, TextAnchor.MiddleLeft);
                ProgressBar(new Rect(x0, y + 62f, cw - 120f, 8f), state.Position(now), current.Duration);
                y += 84f;
            }
            if (player != null && player.Enabled && player.StatusIsProblem && !string.IsNullOrEmpty(player.Status))
            {
                T(new Rect(x0, y, cw, 22f), player.Status, 14, Coral, TextAnchor.MiddleLeft);
                y += 24f;
            }
            return y + 4f;
        }

        /// <summary>
        /// Cliques e roda do rato no vídeo do painel vão para a mesma posição da página oficial (saltar anúncio,
        /// escolha de cookies, aviso de conteúdo). Teclado nunca: as teclas são os atalhos da jukebox.
        /// </summary>
        void ForwardPointer(OnlineMusicPlayer player, Rect frame)
        {
            var e = Event.current;
            if (!frame.Contains(e.mousePosition)) return;
            var n = new Vector2((e.mousePosition.x - frame.x) / frame.width, (e.mousePosition.y - frame.y) / frame.height);
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0: player.Pointer(n, 1); e.Use(); break;
                case EventType.MouseUp when e.button == 0: player.Pointer(n, 3); e.Use(); break;
                case EventType.ScrollWheel when e.delta.y != 0f: player.Pointer(n, 0, -Mathf.Sign(e.delta.y)); e.Use(); break;
            }
        }

        void DrawYouTubeQueue(NetGame net, Rect area)
        {
            var state = net.View.Music.Online;
            var content = new Rect(0f, 0f, area.width - 24f, 260f + state.Queue.Count * 70f + (_jukeboxHostOpen ? 60f : 0f));
            _onlineMusicScroll = GUI.BeginScrollView(area, _onlineMusicScroll, content);
            float w = content.width, y = 0f;

            // ---- adicionar
            bool canAdd = net.CanAddOnlineMusic;
            var field = new Rect(0f, y + 4f, w - 300f, 46f);
            bool valid = YouTubeLink.TryParse(_youtubeUrl, out _, out _);
            Round(new Rect(field.x - 4f, field.y - 4f, field.width + 8f, field.height + 8f),
                string.IsNullOrWhiteSpace(_youtubeUrl) ? Mustard : valid ? GoodGreen : Coral, 12f);
            TextField(field, ref _youtubeUrl, 512, "YouTubeUrl");
            if (string.IsNullOrEmpty(_youtubeUrl) && GUI.GetNameOfFocusedControl() != "YouTubeUrl")
                T(new Rect(field.x + 14f, field.y, field.width - 20f, field.height), !canAdd ? "Adições bloqueadas pelo anfitrião" : _dvdPanel ? "Cola aqui um link de vídeo (passa na TV)" : "Cola aqui um link (vídeo ou playlist)", 17, Muted, TextAnchor.MiddleLeft);
            if (Secondary(new Rect(w - 290f, y + 4f, 140f, 46f), "COLAR", canAdd, 18))
            {
                _youtubeUrl = (GUIUtility.systemCopyBuffer ?? "").Trim();
                if (_youtubeUrl.Length > 512) _youtubeUrl = _youtubeUrl.Substring(0, 512);
            }
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                         GUI.GetNameOfFocusedControl() == "YouTubeUrl";
            if (Primary(new Rect(w - 140f, y + 4f, 140f, 46f), "+ FILA", canAdd && valid, 20) || (enter && canAdd && valid))
            {
                net.AddYouTube(_youtubeUrl, _dvdPanel ? OnlineTrack.FromDvd : OnlineTrack.FromJukebox);
                _youtubeUrl = "";
                if (enter) Event.current.Use();
            }
            y += 56f;
            bool invalid = !string.IsNullOrWhiteSpace(_youtubeUrl) && !valid;
            string hint = invalid ? "Isto não parece um link do YouTube (youtube.com/watch?v=…, youtu.be/…, youtube.com/playlist?list=…)."
                : "Até " + OnlineMusicState.MaxPerPlayer + " por jogador  ·  fila de " + OnlineMusicState.MaxQueue + " partilhada pela jukebox e o DVD  ·  página oficial do YouTube (com os anúncios dele), na TV";
            T(new Rect(2f, y, w, 22f), hint, 14, invalid ? Coral : Muted, TextAnchor.MiddleLeft);
            y += 30f;

            // ---- fila
            T(new Rect(0f, y, w, 30f), "A SEGUIR · " + state.Queue.Count + "/" + OnlineMusicState.MaxQueue, 20, Mustard, TextAnchor.MiddleLeft, true);
            y += 34f;
            if (state.Queue.Count == 0)
            {
                T(new Rect(4f, y, w, 26f), "Fila vazia.", 16, Muted, TextAnchor.MiddleLeft);
                y += 30f;
            }
            for (int i = 0; i < state.Queue.Count; i++)
            {
                var item = state.Queue[i];
                var row = new Rect(0f, y, w, 62f);
                Round(row, i % 2 == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0.02f), 8f);
                DrawThumb(new Rect(row.x + 6f, row.y + 6f, 88f, 50f), item);
                T(new Rect(row.x + 104f, row.y + 4f, w - 200f, 28f), (i + 1) + ". " + item.Title, 17, Ivory, TextAnchor.MiddleLeft, true);
                T(new Rect(row.x + 104f, row.y + 32f, w - 200f, 24f), item.Channel + "  ·  " + (item.Duration > 0 ? MusicTime(item.Duration) : "--:--") + "  ·  " + item.AddedByName +
                    Colored("  ·  via " + OnlineTrack.DeviceName(item.Device), item.Device == OnlineTrack.FromDvd ? Mustard : Muted), 15, Muted, TextAnchor.MiddleLeft);
                if (net.CanRemoveOnlineTrack(i) && Secondary(new Rect(w - 84f, y + 11f, 78f, 40f), "TIRAR", true, 15))
                    net.ControlOnlineMusic(OnlineMusicCommand.Remove, i);
                y += 70f;
            }

            // ---- anfitrião (recolhido até ser preciso)
            if (net.IsServer)
            {
                y += 8f;
                if (Secondary(new Rect(0f, y, w, 42f), _jukeboxHostOpen ? "CONTROLOS DO ANFITRIÃO  ▲" : "CONTROLOS DO ANFITRIÃO  ▼", true, 16)) _jukeboxHostOpen = !_jukeboxHostOpen;
                y += 50f;
                if (_jukeboxHostOpen)
                {
                    float hw = (w - 24f) / 4f;
                    if (Secondary(new Rect(0f, y, hw, 42f), state.HostOnly ? "SÓ ANFITRIÃO" : "PÚBLICA", true, 15)) net.ControlOnlineMusic(OnlineMusicCommand.HostOnly);
                    if (Secondary(new Rect(hw + 8f, y, hw, 42f), state.Locked ? "ADICIONAR: NÃO" : "ADICIONAR: SIM", true, 15)) net.ControlOnlineMusic(OnlineMusicCommand.Lock);
                    if (Secondary(new Rect(2f * (hw + 8f), y, hw, 42f), state.SkipLocked ? "PULAR: SÓ EU" : "PULAR: TODOS", true, 15)) net.ControlOnlineMusic(OnlineMusicCommand.SkipLock);
                    if (DangerBtn(new Rect(3f * (hw + 8f), y, hw, 42f), "LIMPAR FILA", state.Queue.Count > 0, 15)) net.ControlOnlineMusic(OnlineMusicCommand.Clear);
                }
            }
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------------ peças comuns

        void StatusChip(Rect r, bool paused)
        {
            Round(r, paused ? Darker(Coral, 0.7f) : Darker(GoodGreen, 0.55f), r.height * 0.5f);
            T(r, paused ? "EM PAUSA" : "A TOCAR", 15, Ivory, TextAnchor.MiddleCenter, true);
        }

        void ProgressBar(Rect bar, double position, double duration)
        {
            float progress = duration > 0 ? Mathf.Clamp01((float)(position / duration)) : 0f;
            Round(bar, NavyDark, bar.height * 0.5f);
            Round(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * progress), bar.height), Mustard, bar.height * 0.5f);
            T(new Rect(bar.xMax + 10f, bar.y - 10f, 110f, 28f), MusicTime(position) + " / " + (duration > 0 ? MusicTime(duration) : "--:--"), 15, Mustard, TextAnchor.MiddleLeft, true);
        }

        static string MusicTime(double seconds)
        {
            int total = Mathf.Max(0, (int)seconds);
            return total >= 3600 ? (total / 3600) + ":" + (total / 60 % 60).ToString("D2") + ":" + (total % 60).ToString("D2") : (total / 60) + ":" + (total % 60).ToString("D2");
        }
    }
}
