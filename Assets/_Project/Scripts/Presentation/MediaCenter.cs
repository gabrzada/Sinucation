using System.Collections;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.Networking;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Canto da TV no lounge: a televisão de parede que já existia no mapa (RoomBuilder) passa a ser interactiva, com um
    /// móvel e um leitor de DVD por baixo (Blender/media_props.py). Frente em −Z local.
    /// Não tem estado próprio: fila, pausa e relógio são os da jukebox (OnlineMusicState, autoritativo no anfitrião), por
    /// isso o que se escolhe num aparelho aparece no outro. A imagem é a da página oficial do YouTube deste jogador (o
    /// mesmo navegador embutido da jukebox): anúncios, consentimento e falhas são de cada um e nunca param a sala.
    /// </summary>
    public sealed class MediaCenter : MonoBehaviour
    {
        /// <summary>Topo do móvel (a TV é a do mapa, com o centro a RoomBuilder.TvCenterY e as costas na parede, z = 0).</summary>
        public const float ConsoleTop = 0.52f;

        public TvStation Tv { get; private set; }
        public DvdStation Dvd { get; private set; }

        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        internal Material Resolve(string name)
        {
            if (_materials.TryGetValue(name, out var m)) return m;
            switch (name)
            {
                case "navy": m = VisualFactory.Lit(ArtPalette.Navy, 0.38f, 0.3f); break;
                case "suitDark": m = VisualFactory.Lit(ArtPalette.NavyDark, 0.3f, 0.2f); break;
                case "mustard": m = VisualFactory.Lit(ArtPalette.Mustard, 0.45f, 0.25f); break;
                case "coral": m = VisualFactory.Lit(ArtPalette.Coral, 0.4f); break;
                case "teal": m = VisualFactory.Lit(ArtPalette.Teal, 0.4f); break;
                case "ivory": m = VisualFactory.Lit(ArtPalette.Ivory, 0.35f); break;
                case "steel": m = VisualFactory.Lit(ArtPalette.IvoryShade, 0.6f, 0.75f); break;
                case "wood": m = VisualFactory.Textured(TextureFactory.Wood(), Vector2.one * 2f, 0.22f, 0f, 0.25f, new Color(0.70f, 0.43f, 0.22f)); break;
                case "dark": m = VisualFactory.Lit(new Color(0.02f, 0.025f, 0.035f), 0.85f); break;
                default: m = VisualFactory.Lit(ArtPalette.NavyDark, 0.22f); break;
            }
            _materials[name] = m;
            return m;
        }

        public void Build(GameBootstrap game)
        {
            var root = transform;
            var console = FsModels.Get("media", "TvConsole");
            var consoleHolder = LowPoly.Pivot("TvConsole", root, new Vector3(0f, 0f, -0.27f));
            if (console != null) FsModels.Spawn(console, consoleHolder, Vector3.zero, Quaternion.identity, Vector3.one, Resolve);
            else LowPoly.Part(ToyMeshes.RoundedBox(1.5f, ConsoleTop, 0.42f, 0.03f), "Console", consoleHolder, new Vector3(0f, ConsoleTop * 0.5f, 0f), Vector3.one, Resolve("navy"));
            var col = consoleHolder.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, ConsoleTop * 0.5f, 0f);
            col.size = new Vector3(1.5f, ConsoleTop, 0.42f);

            Dvd = new GameObject("DvdPlayer").AddComponent<DvdStation>();
            Dvd.transform.SetParent(root, false);
            Dvd.transform.localPosition = new Vector3(0.2f, ConsoleTop, -0.3f);
            Dvd.Build(this);

            Tv = new GameObject("Tv").AddComponent<TvStation>();
            Tv.transform.SetParent(root, false);
            Tv.transform.localPosition = new Vector3(0f, RoomBuilder.TvCenterY, 0f);
            Tv.Build(this, game);
        }
    }

    /// <summary>
    /// Leitor de DVD: [E] abre o painel partilhado da jukebox no modo DVD (separador YouTube, "pôr no DVD"). O visor
    /// mostra o estado da sala (a tocar / pausa / rádio) — é só um reflexo dele, sem estado próprio.
    /// </summary>
    public sealed class DvdStation : Interactable
    {
        Transform _focus;
        TextMesh _display;
        Renderer _led;
        Material _ledPlay, _ledPause, _ledIdle;
        float _nextDisplay;

        public override Vector3 FocusPoint => _focus.position;
        public override string Prompt => "<b>[E]</b> Leitor de DVD — escolher vídeos para a TV";
        public override bool Zooms => false;
        public override bool CanUse(NetGame net) => net != null && net.HasSnapshot;

        public void Build(MediaCenter media)
        {
            var root = transform;
            var model = FsModels.Get("media", "DvdPlayer");
            if (model != null) FsModels.Spawn(model, root, Vector3.zero, Quaternion.identity, Vector3.one, media.Resolve);
            else LowPoly.Part(ToyMeshes.RoundedBox(0.43f, 0.08f, 0.29f, 0.012f), "Dvd", root, new Vector3(0f, 0.043f, 0f), Vector3.one, media.Resolve("navy"));
            // Visor (texto emissivo sobre a janela escura do modelo) e luz de estado ao lado dos botões.
            _display = VisualFactory.Text(root, "", new Vector3(0.115f, 0.042f, -0.158f), 0.022f, ArtPalette.Teal);
            _ledPlay = VisualFactory.Emissive(ArtPalette.Green, 3f);
            _ledPause = VisualFactory.Emissive(ArtPalette.Mustard, 2.5f);
            _ledIdle = VisualFactory.Emissive(new Color(0.35f, 0.1f, 0.08f), 1.2f);
            var led = LowPoly.Part(ToyMeshes.Sphere, "Led", root, new Vector3(0.012f, 0.03f, -0.157f), Vector3.one * 0.008f, _ledIdle);
            _led = led.GetComponent<Renderer>();
            _led.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _focus = LowPoly.Pivot("Focus", root, new Vector3(0f, 0.05f, -0.15f));
            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.045f, 0f);
            col.size = new Vector3(0.44f, 0.09f, 0.3f);
            InteractionSystem.All.Add(this);
        }

        void OnDestroy() => InteractionSystem.All.Remove(this);

        void Update()
        {
            if (Time.unscaledTime < _nextDisplay) return;
            _nextDisplay = Time.unscaledTime + 0.25f;
            var net = NetGame.Instance;
            string text = "--:--";
            var led = _ledIdle;
            if (net != null && net.HasSnapshot && net.IsSpawned)
            {
                var music = net.View.Music;
                double now = net.NetworkManager.ServerTime.Time;
                if (music.Online.Current != null)
                {
                    var online = music.Online;
                    var player = OnlineMusicPlayer.Instance;
                    bool waiting = online.AdHold || (player != null && player.Enabled && player.AdShowing);
                    text = waiting ? "ESPERA" : (online.Paused ? "PAUSA " : "PLAY ") + TvStation.Clock(online.Position(now));
                    led = online.Paused || waiting ? _ledPause : _ledPlay;
                }
                else if (music.Track >= 0 && music.Track < MusicLibrary.Tracks.Count)
                {
                    text = music.Paused ? "RADIO II" : "RADIO";
                    led = music.Paused ? _ledPause : _ledPlay;
                }
            }
            if (_display.text != text) _display.text = text;
            if (_led.sharedMaterial != led) _led.sharedMaterial = led;
        }
    }

    /// <summary>
    /// Televisão de parede (a do mapa: moldura e ecrã do RoomBuilder; esta classe desenha por cima do ecrã). Mostra o vídeo do item actual (a página oficial do YouTube deste jogador, recortada ao player,
    /// como no painel da jukebox) ou, quando não há vídeo, um cartão honesto: rádio CC0 "só áudio" com a faixa e o
    /// progresso, "[J] ver e ouvir" sem consentimento, estado de carregamento/falha, ou a TV parada. [E] aproxima a câmara
    /// ("ver na TV") com controlos da fila partilhada; cliques no vídeo vão para a página (pular anúncio, cookies).
    /// Também é coluna: a rádio toca aqui em uníssono e o YouTube conta a TV como a coluna mais próxima.
    /// </summary>
    public sealed class TvStation : Interactable
    {
        /// <summary>Ecrã da TV do mapa (RoomBuilder.BuildLounge): 1,55 × 0,87 m (16:9), face a 11 cm da parede.</summary>
        public const float ScreenW = 1.55f, ScreenH = 0.87f, ScreenY = 0f, GlassZ = -0.112f;
        const float WatchDistance = 1.3f, FrameDistance = 14f, TitleSeconds = 7f;

        enum Mode { Idle, Video, YouTubeCard, Radio }

        GameBootstrap _game;
        Transform _screen;
        Mesh _videoMesh;
        Renderer _video, _thumb, _dim, _bg, _barBack, _barFill, _chipBack, _strip;
        Material _videoMat, _thumbMat, _bgMat;
        TextMesh _kicker, _title, _subtitle, _status, _chip, _stripTitle, _stripInfo;
        readonly List<Transform> _bars = new List<Transform>();
        Renderer _led;
        Material _ledVideo, _ledRadio, _ledIdle;
        Rect _uv = new Rect(-1f, 0f, 0f, 0f);
        Texture _shownTex;
        Mode _mode = (Mode)(-1);
        string _trackKey = "";
        float _trackSince;
        bool _onScreen, _texts;
        float _textAt;
        readonly Plane[] _planes = new Plane[6];

        // Miniatura do item actual (só depois do consentimento: liga aos servidores da Google).
        string _thumbId = "";
        Texture2D _thumbTex;

        public override float Range => 4.5f;
        public override Vector3 FocusPoint => _screen.position;
        public override string Prompt => "<b>[E]</b> Ver na TV — de perto, com os controlos da sala";
        public override bool Zooms => true;
        public override bool CanUse(NetGame net) => net != null && net.HasSnapshot;

        public override Pose ZoomPose
        {
            get
            {
                // Em frente ao ecrã (a frente é −Z local), a olhar para ele.
                var position = _screen.position - transform.forward * WatchDistance;
                return new Pose(position, Quaternion.LookRotation(transform.forward, Vector3.up));
            }
        }

        public override Vector3[] ScreenCorners => new[]
        {
            _screen.TransformPoint(new Vector3(-0.5f * ScreenW, -0.5f * ScreenH, 0f)), _screen.TransformPoint(new Vector3(-0.5f * ScreenW, 0.5f * ScreenH, 0f)),
            _screen.TransformPoint(new Vector3(0.5f * ScreenW, 0.5f * ScreenH, 0f)), _screen.TransformPoint(new Vector3(0.5f * ScreenW, -0.5f * ScreenH, 0f))
        };

        /// <summary>O que a TV mostra agora (testes automáticos e registos).</summary>
        public string Showing => _mode.ToString();

        public void Build(MediaCenter media, GameBootstrap game)
        {
            _game = game;
            var root = transform;

            _screen = LowPoly.Pivot("Screen", root, new Vector3(0f, ScreenY, GlassZ));
            // Camadas do ecrã por ordem de desenho fixa (filas de render), para não trocarem de ordem vistas de lado.
            _bgMat = Layer("Background", 0, ArtPalette.NavyDark, out _bg);
            _bg.transform.localScale = new Vector3(ScreenW, ScreenH, 1f);
            _thumbMat = Layer("Thumbnail", 1, Color.white, out _thumb);
            _dim = Quad("Dim", 2, new Color(0f, 0f, 0f, 0.6f), new Vector3(0f, 0f, -0.001f), new Vector2(ScreenW, ScreenH));

            _videoMesh = new Mesh { name = "TvVideo" };
            _videoMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            _videoMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            _videoMesh.triangles = new[] { 0, 3, 1, 3, 0, 2 };
            _videoMesh.RecalculateBounds();
            var video = new GameObject("Video");
            video.transform.SetParent(_screen, false);
            video.transform.localPosition = new Vector3(0f, 0f, -0.0015f);
            video.AddComponent<MeshFilter>().sharedMesh = _videoMesh;
            _video = video.AddComponent<MeshRenderer>();
            _videoMat = VisualFactory.Unlit(new Color(0.94f, 0.94f, 0.94f));
            _videoMat.renderQueue = 2993;
            _video.sharedMaterial = _videoMat;
            Quiet(_video);

            // Cartão (quando não há vídeo): textos 3D da fonte do jogo, barra de progresso, chip e equalizador.
            float left = -0.5f * ScreenW + 0.08f;
            _kicker = Label(new Vector3(left, 0.29f, -0.004f), 0.042f, ArtPalette.Mustard, TextAnchor.MiddleLeft, true);
            _title = Label(new Vector3(left, 0.13f, -0.004f), 0.1f, ArtPalette.Ivory, TextAnchor.MiddleLeft, true);
            _subtitle = Label(new Vector3(left, 0.02f, -0.004f), 0.042f, ArtPalette.IvoryShade, TextAnchor.MiddleLeft, false);
            _status = Label(new Vector3(left, -0.21f, -0.004f), 0.045f, ArtPalette.Ivory, TextAnchor.MiddleLeft, false);
            _barBack = Quad("BarBack", 3, ArtPalette.NavyLight, new Vector3(0f, -0.31f, -0.002f), new Vector2(ScreenW - 0.16f, 0.022f));
            _barFill = Quad("BarFill", 4, ArtPalette.Mustard, new Vector3(0f, -0.31f, -0.003f), new Vector2(ScreenW - 0.16f, 0.022f));
            _chipBack = Quad("ChipBack", 6, ArtPalette.Coral, new Vector3(0.5f * ScreenW - 0.2f, 0.3f, -0.002f), new Vector2(0.3f, 0.075f));
            _chip = Label(new Vector3(0.5f * ScreenW - 0.2f, 0.3f, -0.004f), 0.04f, ArtPalette.Ivory, TextAnchor.MiddleCenter, true);
            for (int i = 0; i < 12; i++)
            {
                var bar = Quad("Eq", 4, Color.Lerp(ArtPalette.Teal, ArtPalette.Mustard, i / 11f), Vector3.zero, Vector2.one);
                bar.transform.localPosition = new Vector3(0.12f + i * 0.045f, -0.16f, -0.003f);
                _bars.Add(bar.transform);
            }
            // Faixa de título por cima do vídeo (início da faixa e pausa).
            _strip = Quad("Strip", 5, new Color(0.04f, 0.06f, 0.1f, 0.82f), new Vector3(0f, -0.5f * ScreenH + 0.075f, -0.003f), new Vector2(ScreenW, 0.15f));
            _stripTitle = Label(new Vector3(left, -0.5f * ScreenH + 0.1f, -0.005f), 0.05f, ArtPalette.Ivory, TextAnchor.MiddleLeft, true);
            _stripInfo = Label(new Vector3(left, -0.5f * ScreenH + 0.045f, -0.005f), 0.032f, ArtPalette.Mustard, TextAnchor.MiddleLeft, false);

            // Luz de estado na moldura, por baixo do canto direito do ecrã.
            _ledVideo = VisualFactory.Emissive(ArtPalette.Green, 3f);
            _ledRadio = VisualFactory.Emissive(ArtPalette.Mustard, 2.5f);
            _ledIdle = VisualFactory.Emissive(new Color(0.35f, 0.1f, 0.08f), 1.2f);
            var led = LowPoly.Part(ToyMeshes.Sphere, "Led", root, new Vector3(0.72f, -0.468f, -0.104f), Vector3.one * 0.016f, _ledIdle);
            _led = led.GetComponent<Renderer>();
            Quiet(_led);

            var col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, -0.06f);
            col.size = new Vector3(1.7f, 1.0f, 0.12f);
            InteractionSystem.All.Add(this);

            // Coluna: a rádio CC0 em uníssono com a jukebox; o YouTube conta a TV como coluna mais próxima.
            var speaker = gameObject.AddComponent<AudioSource>();
            speaker.spatialBlend = 1f;
            speaker.minDistance = 2f;
            speaker.maxDistance = 9f;
            speaker.rolloffMode = AudioRolloffMode.Linear;
            game.Jukebox?.AddSpeaker(speaker);
            OnlineMusicPlayer.Instance?.AddSpeaker(transform, new Vector3(0f, ScreenY, -0.1f));
            SetMode(Mode.Idle);
        }

        void OnDestroy()
        {
            InteractionSystem.All.Remove(this);
            if (_thumbTex != null) Destroy(_thumbTex);
            if (_videoMesh != null) Destroy(_videoMesh);
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        Material Layer(string name, int order, Color color, out Renderer renderer)
        {
            renderer = Quad(name, order, color, new Vector3(0f, 0f, -0.0005f * order), new Vector2(ScreenW, ScreenH));
            return renderer.sharedMaterial;
        }

        Renderer Quad(string name, int order, Color color, Vector3 position, Vector2 size)
        {
            var m = VisualFactory.Unlit(color);
            // Abaixo da fila do texto 3D (3000): o texto fica sempre por cima, e as camadas na ordem dada.
            m.renderQueue = 2990 + order;
            var go = VisualFactory.Primitive(PrimitiveType.Quad, name, _screen, position, new Vector3(size.x, size.y, 1f), m);
            var r = go.GetComponent<Renderer>();
            Quiet(r);
            return r;
        }

        TextMesh Label(Vector3 position, float height, Color color, TextAnchor anchor, bool title)
        {
            var tm = VisualFactory.Text(_screen, "", position, height, color, anchor, title);
            tm.alignment = anchor == TextAnchor.MiddleCenter ? TextAlignment.Center : TextAlignment.Left;
            return tm;
        }

        static void Set(TextMesh tm, string text, Color? color = null)
        {
            text ??= "";
            if (tm.text != text) tm.text = text;
            if (color.HasValue && tm.color != color.Value) tm.color = color.Value;
        }

        static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        void SetMode(Mode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            _texts = true;
            bool card = mode != Mode.Video;
            Show(_video, mode == Mode.Video);
            Show(_kicker, card); Show(_title, card); Show(_subtitle, card); Show(_status, card);
            Show(_barBack, mode == Mode.Radio || mode == Mode.YouTubeCard);
            Show(_barFill, mode == Mode.Radio || mode == Mode.YouTubeCard);
            foreach (var b in _bars) Show(b, mode == Mode.Radio);
            if (mode != Mode.YouTubeCard) { Show(_thumb, false); Show(_dim, false); }
            Show(_strip, false); Show(_stripTitle, false); Show(_stripInfo, false);
            Show(_chipBack, false); Show(_chip, false);
            VisualFactory.SetColor(_bgMat, mode == Mode.Video ? Color.black : mode == Mode.Radio ? ArtPalette.Navy : ArtPalette.NavyDark);
            _led.sharedMaterial = mode == Mode.Video || mode == Mode.YouTubeCard ? _ledVideo : mode == Mode.Radio ? _ledRadio : _ledIdle;
        }

        void Chip(string text, Color color)
        {
            bool on = !string.IsNullOrEmpty(text);
            Show(_chipBack, on); Show(_chip, on);
            if (!on) return;
            Set(_chip, text);
            VisualFactory.SetColor(_chipBack.sharedMaterial, color);
            _chipBack.transform.localScale = new Vector3(Mathf.Max(0.22f, 0.03f + text.Length * 0.026f), 0.075f, 1f);
        }

        void Progress(double position, double duration)
        {
            float width = ScreenW - 0.16f;
            float t = duration > 0 ? Mathf.Clamp01((float)(position / duration)) : 0f;
            _barFill.transform.localScale = new Vector3(Mathf.Max(0.001f, width * t), 0.022f, 1f);
            _barFill.transform.localPosition = new Vector3(-0.5f * width + 0.5f * width * t, -0.31f, -0.003f);
        }

        public static string Clock(double seconds)
        {
            int total = Mathf.Max(0, (int)seconds);
            return total >= 3600 ? (total / 3600) + ":" + (total / 60 % 60).ToString("D2") + ":" + (total % 60).ToString("D2") : (total / 60) + ":" + (total % 60).ToString("D2");
        }

        // ================================================================== ciclo

        void LateUpdate()
        {
            var net = NetGame.Instance;
            var cam = _game.Rig != null ? _game.Rig.Camera : null;
            bool near = cam != null && Vector3.Distance(cam.transform.position, _screen.position) < FrameDistance;
            if (near) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            _onScreen = near && GeometryUtility.TestPlanesAABB(_planes, _bg.bounds);
            // Textos (e as strings que custam) 4 vezes por segundo; barras e imagem a cada frame.
            _texts = Time.unscaledTime >= _textAt;
            if (_texts) _textAt = Time.unscaledTime + 0.25f;
            if (net == null || !net.HasSnapshot || !net.IsSpawned) { Idle(); return; }
            var music = net.View.Music;
            var online = music.Online;
            double now = net.NetworkManager.ServerTime.Time;
            if (online.Current != null) ShowYouTube(net, online, now);
            else if (music.Track >= 0 && music.Track < MusicLibrary.Tracks.Count) ShowRadio(music, now);
            else Idle();
        }

        void Idle()
        {
            SetMode(Mode.Idle);
            _trackKey = "";
            if (!_texts) return;
            Set(_kicker, "CAÇAPA TV");
            Set(_title, "Nada a passar");
            Set(_subtitle, "Escolhe um vídeo no leitor de DVD (ou na jukebox) — a fila é a mesma.");
            Set(_status, "");
        }

        void ShowRadio(MusicView music, double now)
        {
            SetMode(Mode.Radio);
            _trackKey = "";
            var track = MusicLibrary.Tracks[music.Track];
            double position = music.RadioPosition(now);
            float length = track.Clip != null ? track.Clip.length : 1f;
            if (_texts)
            {
                Set(_kicker, "RÁDIO CAÇAPA  ·  SÓ ÁUDIO");
                Set(_title, OnlineTrack.Clip(track.Title, 30));
                Set(_subtitle, track.Author + "  ·  faixa CC0 sem vídeo (OpenGameArt)");
                Set(_status, Clock(position) + " / " + Clock(length) + (music.Paused ? "   ·   EM PAUSA" : ""), ArtPalette.Ivory);
            }
            Progress(position, length);
            Chip(music.Paused ? "EM PAUSA" : "", ArtPalette.Coral);
            // Equalizador decorativo (parado em pausa): a TV não promete imagem para uma faixa só de áudio.
            float t = (float)now;
            for (int i = 0; i < _bars.Count; i++)
            {
                float h = music.Paused ? 0.01f : 0.02f + 0.16f * Mathf.PerlinNoise(t * 2.2f + i * 0.37f, i * 1.7f);
                _bars[i].localScale = new Vector3(0.03f, h, 1f);
                _bars[i].localPosition = new Vector3(0.12f + i * 0.045f, -0.16f + h * 0.5f, -0.003f);
            }
        }

        void ShowYouTube(NetGame net, OnlineMusicState online, double now)
        {
            var current = online.Current;
            var player = OnlineMusicPlayer.Instance;
            if (_trackKey != current.VideoId) _trackSince = Time.unscaledTime;
            _trackKey = current.VideoId;
            bool enabled = player != null && player.Enabled;
            bool zoomed = _game.Interaction != null && _game.Interaction.Active == this;
            // Pede a imagem à página só com a TV à vista e perto (poupa a cópia da textura no resto do tempo).
            if (enabled && (_onScreen || zoomed)) player.RequestFrame(zoomed ? ProjectedWidth() : 0f);

            if (enabled && player.TryGetFrame(out var tex, out var uv, out float aspect))
            {
                SetMode(Mode.Video);
                if (tex != _shownTex) { VisualFactory.SetTexture(_videoMat, tex); _shownTex = tex; }
                if (uv != _uv)
                {
                    _uv = uv;
                    _videoMesh.uv = new[]
                    {
                        new Vector2(uv.x, uv.y), new Vector2(uv.x + uv.width, uv.y),
                        new Vector2(uv.x, uv.y + uv.height), new Vector2(uv.x + uv.width, uv.y + uv.height)
                    };
                }
                var fit = OnlineMusicPlayer.Fit(new Rect(0f, 0f, ScreenW, ScreenH), aspect);
                _video.transform.localScale = new Vector3(fit.width, fit.height, 1f);
                bool waiting = player.WaitingForOthers;
                Chip(player.AdShowing ? "ANÚNCIO" : waiting ? "A ESPERAR OS OUTROS" : online.Paused ? "EM PAUSA" : "",
                    player.AdShowing || waiting ? ArtPalette.MustardDark : ArtPalette.Coral);
                bool strip = online.Paused || Time.unscaledTime - _trackSince < TitleSeconds || player.ShowsWholePage;
                Show(_strip, strip); Show(_stripTitle, strip); Show(_stripInfo, strip);
                if (strip && _texts)
                {
                    Set(_stripTitle, player.ShowsWholePage ? "O YouTube pede uma escolha — [E] e responde no ecrã" : OnlineTrack.Clip(current.Title, 52));
                    Set(_stripInfo, Info(current) + "   ·   " + Clock(online.Position(now)) + (current.Duration > 0 ? " / " + Clock(current.Duration) : ""));
                }
                return;
            }

            // Sem imagem (ainda a carregar, sem consentimento, falhou): cartão com o que a sala está a ver.
            SetMode(Mode.YouTubeCard);
            bool consent = _game.Settings.YouTubeConsent || enabled;
            var thumb = consent ? Thumbnail(current) : null;
            Show(_thumb, thumb != null); Show(_dim, thumb != null);
            if (thumb != null && _thumbMat.mainTexture != thumb) VisualFactory.SetTexture(_thumbMat, thumb);
            Progress(online.Position(now), current.Duration);
            Chip(online.Paused ? "EM PAUSA" : "", ArtPalette.Coral);
            if (!_texts) return;
            Set(_kicker, "YOUTUBE  ·  A SALA ESTÁ A VER");
            Set(_title, OnlineTrack.Clip(current.Title, 34));
            Set(_subtitle, Info(current));
            string status; Color color = ArtPalette.Ivory;
            if (player == null) status = "";
            else if (!enabled && !player.Failed) { status = "[J] VER E OUVIR AQUI  ·  liga à página oficial do YouTube (Google)"; color = ArtPalette.Mustard; }
            else if (player.Failed || player.StatusIsProblem) { status = OnlineTrack.Clip(player.Status, 70) + "  ·  ABRIR NO YOUTUBE no DVD"; color = ArtPalette.Coral; }
            else status = online.AdHold ? "A abrir… a sala espera pelos anúncios de todos" : "A abrir a página oficial do YouTube…";
            Set(_status, status, color);
        }

        static string Info(OnlineTrack track) =>
            (string.IsNullOrEmpty(track.Channel) ? "" : OnlineTrack.Clip(track.Channel, 26) + "  ·  ") + "pedido por " + track.AddedByName + " via " + OnlineTrack.DeviceName(track.Device);

        /// <summary>Largura do ecrã em píxeis (vista de perto): a página é pedida a essa largura, para a imagem ficar nítida.</summary>
        float ProjectedWidth()
        {
            var cam = _game.Rig != null ? _game.Rig.Camera : null;
            if (cam == null) return 0f;
            var a = cam.WorldToScreenPoint(_screen.TransformPoint(new Vector3(-0.5f * ScreenW, 0f, 0f)));
            var b = cam.WorldToScreenPoint(_screen.TransformPoint(new Vector3(0.5f * ScreenW, 0f, 0f)));
            return Mathf.Abs(b.x - a.x);
        }

        /// <summary>
        /// Ponto do ecrã (píxeis, origem em baixo) → posição no vídeo desenhado (0–1, origem em cima), para reencaminhar
        /// cliques para a página. Falso fora do vídeo ou quando a TV não mostra vídeo.
        /// </summary>
        public bool ScreenToVideo(Vector2 screenPixels, out Vector2 normalized)
        {
            normalized = default;
            var cam = _game.Rig != null ? _game.Rig.Camera : null;
            if (cam == null || _mode != Mode.Video) return false;
            var ray = cam.ScreenPointToRay(screenPixels);
            var plane = new Plane(_video.transform.forward, _video.transform.position);
            if (!plane.Raycast(ray, out float enter)) return false;
            var local = _video.transform.InverseTransformPoint(ray.GetPoint(enter));
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
            normalized = new Vector2(local.x + 0.5f, 0.5f - local.y);
            return true;
        }

        Texture2D Thumbnail(OnlineTrack track)
        {
            if (track.VideoId != _thumbId)
            {
                _thumbId = track.VideoId;
                if (_thumbTex != null) Destroy(_thumbTex);
                _thumbTex = null;
                StartCoroutine(LoadThumbnail(track.VideoId, track.Thumbnail));
            }
            return _thumbTex;
        }

        IEnumerator LoadThumbnail(string id, string url)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || id != _thumbId) yield break;
                var tex = DownloadHandlerTexture.GetContent(request);
                if (tex == null) yield break;
                tex.hideFlags = HideFlags.DontSave;
                _thumbTex = tex;
            }
        }
    }
}
