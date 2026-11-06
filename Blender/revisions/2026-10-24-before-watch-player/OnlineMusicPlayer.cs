using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FriendSlop.Net;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using Gree.UnityWebView;
#endif

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Um único player oficial do YouTube por cliente (WebView2 fora do ecrã, desenhado pequeno no canto): só recebe
    /// ID, posição, pausa e volume. O áudio sai pelo player; o jogo aproxima o som espacial da jukebox com volume por
    /// distância e oclusão (paredes). Falhas (sem WebView2, sem internet, vídeo bloqueado) ficam aqui — o jogo segue.
    /// </summary>
    public sealed class OnlineMusicPlayer : MonoBehaviour
    {
        [Serializable] sealed class PlayerCommand { public string video; public double position; public bool paused; public int volume; }
        [Serializable] sealed class PlayerEvent { public string type, video, title, author, list; public int code; public double duration; public string[] ids; }

        const float PlayerWidth = 356f, PlayerHeight = 200f;
        const float FullVolumeDistance = 2.5f;

        GameBootstrap _game;
        TcpListener _server;
        string _pageUrl;
        bool _ready, _probeReady, _failed;
        float _nextSync, _smoothedVolume = -1f;
        int _width, _height, _bottom = -1;
        string _expandList;
        Action<List<string>, string> _expandDone;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        public string Status { get; private set; } = "O YouTube usa o player oficial, visível, e liga aos serviços da Google quando é ativado.";
        public bool Enabled => _browser != null && !_failed;
        public bool Failed => _failed;
        /// <summary>Volume efectivo enviado ao player (0–1), já com distância e oclusão — para a UI.</summary>
        public float SpatialVolume { get; private set; }
        public bool Occluded { get; private set; }
        public int PlayerState { get; private set; } = -1;
        /// <summary>O navegador bloqueou a reprodução com som: a HUD mostra "[J] ATIVAR SOM".</summary>
        public bool AutoplayBlocked { get; private set; }
        public double PlayerTime { get; private set; }
        public string PlayerDiagnostic { get; private set; } = "";
        float _nextStateLog;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        WebViewObject _browser;
#else
        object _browser;
#endif
        public static OnlineMusicPlayer Instance { get; private set; }

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            Instance = this;
        }

        /// <summary>Activa o player (consentimento explícito do jogador, lembrado para as próximas sessões).</summary>
        public void EnablePlayer()
        {
            if (_game != null && !_game.Settings.YouTubeConsent)
            {
                _game.Settings.YouTubeConsent = true;
                _game.Settings.Save();
            }
            _failed = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser != null) return;
            try
            {
                string html = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Jukebox/player.html"));
                // Página servida por HTTP local (origem válida para a IFrame API; file:// é recusado pelo YouTube).
                string path = "/" + Guid.NewGuid().ToString("N") + "/player.html";
                _server = new TcpListener(IPAddress.Loopback, 0);
                _server.Start(4);
                _pageUrl = "http://127.0.0.1:" + ((IPEndPoint)_server.LocalEndpoint).Port + path;
                _ = ServePage(_server, path, html);
                // O jogador activou o player explicitamente: deixa o vídeo arrancar sem outro clique dentro do player
                // (sem isto o Chromium bloqueia a reprodução automática com som).
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
                _browser = new GameObject("OfficialYouTubePlayer").AddComponent<WebViewObject>();
                _browser.transform.SetParent(transform, false);
                _browser.Init(cb: OnMessage, err: e => Fail("Falha no player (" + e + "); a partida continua."));
                // Um frame de vídeo a cada 3 do jogo chega para o player pequeno e poupa GPU/CPU (RT + upscalers).
                _browser.bitmapRefreshCycle = 3;
                _browser.LoadURL(_pageUrl);
                Resize();
                _browser.SetVisibility(false);
                Status = "A carregar o player oficial…";
                Debug.Log("[Jukebox] player oficial a carregar em " + _pageUrl);
            }
            catch (Exception e)
            {
                Fail("Player indisponível: instale/atualize o Microsoft Edge WebView2 Runtime. (" + e.GetType().Name + ")");
            }
#else
            Status = "O player integrado está disponível na versão Windows.";
            _failed = true;
#endif
        }

        void Fail(string message)
        {
            Status = message;
            _failed = true;
            _ready = _probeReady = false;
            Debug.LogWarning("[Jukebox] " + message);
            FinishExpand(null, "O player do YouTube falhou.");
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser != null) Destroy(_browser.gameObject);
            _browser = null;
#endif
            _server?.Stop();
            _server = null;
            if (YouTubeCatalog.PlaylistExpander == (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand) YouTubeCatalog.PlaylistExpander = null;
        }

        static async Task ServePage(TcpListener listener, string path, string html)
        {
            try
            {
                while (true)
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[4096];
                        var read = stream.ReadAsync(buffer, 0, buffer.Length);
                        if (await Task.WhenAny(read, Task.Delay(2000)) != read) continue;
                        int count = await read;
                        string request = Encoding.ASCII.GetString(buffer, 0, count);
                        bool valid = request.StartsWith("GET " + path + " HTTP/", StringComparison.Ordinal);
                        byte[] body = Encoding.UTF8.GetBytes(valid ? html : "Not found");
                        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (valid ? "200 OK" : "404 Not Found") +
                            "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(head, 0, head.Length);
                        await stream.WriteAsync(body, 0, body.Length);
                    }
                }
            }
            catch (Exception) { /* O listener fecha com o objecto; a música nunca bloqueia o jogo. */ }
        }

        void OnMessage(string json)
        {
            PlayerEvent e;
            try { e = JsonUtility.FromJson<PlayerEvent>(json); } catch (Exception) { return; }
            if (e == null) return;
            var net = NetGame.Instance;
            var current = net != null && net.HasSnapshot ? net.View.Music.Online.Current : null;
            switch (e.type)
            {
                case "ready":
                    _ready = true;
                    _nextSync = 0f;
                    Status = "Player pronto.";
                    Debug.Log("[Jukebox] player pronto");
                    break;
                case "state":
                    // code = estado do YouTube (1 a tocar, 2 pausa, 3 a carregar, 5 pronto, -1 não iniciado); duration = tempo actual.
                    PlayerState = e.code;
                    PlayerTime = e.duration;
                    PlayerDiagnostic = "estado " + e.code + " · " + e.duration.ToString("0") + " s · vol " + e.title + (e.author == "true" ? " · MUDO" : "");
                    if (Time.unscaledTime >= _nextStateLog) { _nextStateLog = Time.unscaledTime + 10f; Debug.Log("[Jukebox] player: " + PlayerDiagnostic); }
                    break;
                case "probeReady":
                    _probeReady = true;
                    break;
                case "blocked":
                    AutoplayBlocked = true;
                    Status = "O navegador bloqueou a reprodução automática com som: carrega [J] (ou ▶ no player da jukebox).";
                    break;
                case "meta":
                    AutoplayBlocked = false;
                    Debug.Log("[Jukebox] a tocar " + e.video + " (" + e.duration.ToString("0") + " s)");
                    if (current != null && current.VideoId == e.video && current.Duration <= 0 && e.duration > 0)
                        net.ReportOnlineMusicDuration(e.video, e.duration);
                    Status = "A tocar no player oficial.";
                    break;
                case "error":
                case "ended":
                    if (current == null || e.video != current.VideoId) return;
                    if (e.type == "error")
                    {
                        // 2 pedido inválido, 5 erro HTML5, 100 removido/privado, 101/150 dono não permite incorporar.
                        Status = e.code == 100 ? "Vídeo removido ou privado." : e.code == 101 || e.code == 150 || e.code == 153 ? "A editora bloqueia este vídeo fora do YouTube — a procurar automaticamente outra versão da mesma música…" : "YouTube: erro " + e.code + " neste vídeo.";
                        Debug.LogWarning("[Jukebox] erro " + e.code + " em " + e.video);
                    }
                    net.ReportOnlineMusicEnd(e.video, e.type == "error", e.code);
                    break;
                case "expanded":
                    if (e.list != _expandList) return;
                    var ids = new List<string>();
                    if (e.ids != null) ids.AddRange(e.ids);
                    FinishExpand(ids, ids.Count == 0 ? "Playlist vazia, privada ou indisponível." : null);
                    break;
            }
        }

        /// <summary>
        /// Depois de um bloqueio de autoplay: acção explícita do jogador (tecla/botão) — volta a pedir a reprodução e,
        /// se ainda assim o som for recusado, começa sem som e liga-o assim que o navegador deixar.
        /// </summary>
        public void RetryPlayback()
        {
            AutoplayBlocked = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            _browser?.EvaluateJS("window.fsUnblock && fsUnblock()");
#endif
            _nextSync = 0f;
        }

        /// <summary>Lista os vídeos de uma playlist no player escondido ("probe") — sem chave da API.</summary>
        IEnumerator Expand(string list, int max, Action<List<string>, string> done)
        {
            FinishExpand(null, "Pedido substituído.");
            _expandList = list;
            _expandDone = done;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // O ID já foi validado por YouTubeLink (só letras, dígitos, "-" e "_"): seguro dentro de aspas.
            _browser?.EvaluateJS("window.fsExpand && fsExpand('" + list + "'," + max + ")");
#endif
            float until = Time.unscaledTime + 12f;
            while (_expandDone == done && Time.unscaledTime < until) yield return null;
            if (_expandDone == done) FinishExpand(null, "O YouTube não respondeu a tempo (rede).");
        }

        void FinishExpand(List<string> ids, string error)
        {
            var done = _expandDone;
            _expandDone = null;
            _expandList = null;
            done?.Invoke(ids, error);
        }

        void Update()
        {
            var net = NetGame.Instance;
            var state = net != null && net.HasSnapshot && net.IsSpawned ? net.View.Music.Online : null;
            if (net != null) net.LocalOnlinePlayerActive = _ready && !_failed;

            // Quem já aceitou o player numa sessão anterior não precisa de carregar outra vez no botão.
            if (_browser == null && !_failed && state?.Current != null && _game != null && _game.Settings.YouTubeConsent) EnablePlayer();

            // Só o anfitrião resolve playlists sem chave (é quem adiciona à fila).
            bool offerExpander = net != null && net.IsServer && _probeReady && !_failed;
            var expander = (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand;
            if (offerExpander) YouTubeCatalog.PlaylistExpander = expander;
            else if (YouTubeCatalog.PlaylistExpander == expander) YouTubeCatalog.PlaylistExpander = null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser == null) return;
            bool playing = state?.Current != null;
            // O player oficial só aparece dentro do painel da Jukebox (o HUD chama ShowIn em cada frame em que o
            // desenha). Fora do painel a imagem não é desenhada nem capturada — não tapa o jogo nem gasta GPU.
            bool inPanel = playing && Time.frameCount - _panelFrame <= 2;
            _browser.SetVisibility(playing);
            // O HUD desenha a imagem dentro do painel (o plugin desenharia por baixo do painel, que o tapava).
            _browser.drawTexture = false;
            _browser.captureTexture = inPanel;
            if (inPanel) PlaceAt(_panelRect);
            if (!_ready || Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 0.25f;
            if (!playing)
            {
                _browser.EvaluateJS("window.fsPause && fsPause()");
                return;
            }
            float target = TargetVolume();
            _smoothedVolume = _smoothedVolume < 0f ? target : Mathf.MoveTowards(_smoothedVolume, target, 0.25f);
            SpatialVolume = _smoothedVolume;
            var command = new PlayerCommand
            {
                video = state.Current.VideoId,
                position = state.Position(net.NetworkManager.ServerTime.Time),
                paused = state.Paused,
                volume = Mathf.RoundToInt(_smoothedVolume * 100f)
            };
            _browser.EvaluateJS("window.fsSync && fsSync(" + JsonUtility.ToJson(command) + ")");
#endif
        }

        /// <summary>
        /// "Áudio espacial" possível com um player externo: volume inverso à distância (cheio até 2,5 m, ~25% a 10 m)
        /// e abafado quando há paredes/objectos grandes entre a câmara e a jukebox. Volume individual (0–100%) e mute
        /// são locais — não mudam o que os outros ouvem.
        /// </summary>
        float TargetVolume()
        {
            var settings = _game.Settings;
            if (settings.JukeboxMuted) return 0f;
            var cam = _game.Rig != null && _game.Rig.Camera != null ? _game.Rig.Camera.transform : null;
            float distance = cam != null ? Vector3.Distance(cam.position, transform.position + Vector3.up * 1.1f) : FullVolumeDistance;
            float attenuation = FullVolumeDistance / Mathf.Max(FullVolumeDistance, distance);
            Occluded = cam != null && IsOccluded(cam.position);
            if (Occluded) attenuation *= 0.55f;
            return Mathf.Clamp01(settings.MasterVolume * settings.JukeboxVolume * attenuation);
        }

        bool IsOccluded(Vector3 from)
        {
            Vector3 to = transform.position + Vector3.up * 1.1f;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / length, _hits, length - 0.6f, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform) || c is CharacterController || c is CapsuleCollider) continue;
                // Só bloqueia o que é grande (paredes, balcão, máquinas); tacos e bolas não abafam a música.
                if (c.bounds.size.magnitude > 1.2f) return true;
            }
            return false;
        }

        void Resize()
        {
            _width = Screen.width;
            _height = Screen.height;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // 356 × 200 (16:9; o YouTube pede pelo menos 200 × 200 px de player visível).
            int bottom = Mathf.Max(16, _bottom);
            _browser.SetMargins(Mathf.Max(0, _width - 16 - (int)PlayerWidth), Mathf.Max(0, _height - bottom - (int)PlayerHeight), 16, bottom);
#endif
        }

        Rect _panelRect, _placed;
        int _panelFrame = -10;

        /// <summary>Rectângulo do vídeo no painel da Jukebox, em píxeis de ecrã (origem no canto superior esquerdo).</summary>
        public void ShowIn(Rect screenRect)
        {
            _panelRect = screenRect;
            _panelFrame = Time.frameCount;
        }

        void PlaceAt(Rect r)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (r == _placed && _width == Screen.width && _height == Screen.height) return;
            _placed = r;
            _width = Screen.width;
            _height = Screen.height;
            _browser.SetMargins(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y),
                Mathf.Max(0, Mathf.RoundToInt(Screen.width - r.xMax)), Mathf.Max(0, Mathf.RoundToInt(Screen.height - r.yMax)));
#endif
        }

        /// <summary>Último fotograma do player oficial (null enquanto carrega).</summary>
        public Texture VideoTexture
        {
            get
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return _browser != null ? _browser.CurrentTexture : null;
#else
                return null;
#endif
            }
        }

        public Rect ScreenRect => new Rect(Screen.width - 16f - PlayerWidth, Screen.height - Mathf.Max(16, _bottom) - PlayerHeight, PlayerWidth, PlayerHeight);

        void OnDestroy()
        {
            FinishExpand(null, "Player fechado.");
            _server?.Stop();
            var expander = (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand;
            if (YouTubeCatalog.PlaylistExpander == expander) YouTubeCatalog.PlaylistExpander = null;
            if (Instance == this) Instance = null;
        }
    }
}
