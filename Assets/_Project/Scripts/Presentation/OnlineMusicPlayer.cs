using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using Gree.UnityWebView;
#endif

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Um único navegador embutido (WebView2) por cliente, aberto na página oficial do vídeo (youtube.com/watch) —
    /// a página oficial toca o que as editoras bloqueiam nos players incorporados (erros 101/150/153). O jogo só manda
    /// ID, posição, pausa e volume (StreamingAssets/Jukebox/watch.js); a página não é alterada: o painel da jukebox
    /// recorta a zona do player ("máscara" com o mesmo aspecto do mini player) e reencaminha os cliques para lá.
    /// Anúncios do YouTube correm como no site. O áudio sai pela página; o jogo aproxima o som espacial da jukebox com
    /// volume por distância e oclusão. Falhas (sem WebView2, sem internet, vídeo que pede login) ficam aqui — o jogo
    /// segue, e o painel oferece abrir o vídeo no YouTube.
    /// </summary>
    public sealed class OnlineMusicPlayer : MonoBehaviour
    {
        [Serializable] sealed class PlayerCommand { public string video; public double position; public bool paused, hold; public int volume; }
        [Serializable] sealed class PlayerEvent { public string type, video, title, author, list; public int code, vw, vh; public double duration, time; public bool ad, ready; public int[] rect, skip; public string[] ids; public int pv; public bool pm, em; public double ev, ldb; }

        /// <summary>
        /// Curva da distância igual à da rádio da jukebox (AudioSource 55 % 3D, linear de 4 a 30 m): o mesmo controlo de
        /// volume soa igual nas duas. A antiga (2,5/d e ×0,55 com paredes) deixava o vídeo a ~46 % junto à mesa com a
        /// jukebox a 100 %.
        /// </summary>
        public const float FullVolumeDistance = 4f, SilentDistance = 30f, SpatialShare = 0.55f, OccludedFactor = 0.8f;
        const float LegacyFullDistance = 2.5f, LegacyOccluded = 0.55f;
        /// <summary>QA (-fsVolumeLegacy): curva antiga, para comparar antes/depois na mesma build.</summary>
        public static bool LegacyVolume;
        /// <summary>Larguras da página (layout de uma coluna do YouTube: player 16:9 a toda a largura, no topo).</summary>
        const int MinPageWidth = 656, MaxPageWidth = 976, PageStep = 80;
        const float PageAspect = 0.88f, LoadTimeout = 20f, IdleUnloadSeconds = 90f, ClickOpensFor = 2f;

        GameBootstrap _game;
        string _controller;
        bool _failed;
        float _nextSync, _smoothedVolume = -1f, _nextInstall;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        // Página carregada e o que ela reporta (watch.js, mensagem "page" a cada sincronização).
        string _loadedVideo = "", _pageVideo = "", _endedVideo = "", _erroredVideo = "";
        float _loadedAt, _lastPageAt = -100f, _retryAt = -1f, _idleSince = -1f, _mismatchSince = -1f, _lastClickAt = -100f, _nextStateLog;
        int _loadAttempts, _pageW = MinPageWidth, _pageH = Mathf.RoundToInt(MinPageWidth * PageAspect), _reportedW, _reportedH;
        RectInt _playerRect, _skipRect;
        bool _hasPlayerRect, _pageAlive, _hasSkip;
        // Música pronta (carregada, sem anúncio) e o que já se disse ao anfitrião sobre estar ocupado.
        string _readyVideo = "", _busyReported = "";
        float _trackSince;
        YouTubePlaylistProbe _probe;

        public string Status { get; private set; } = "O YouTube toca na página oficial (com os anúncios do YouTube) e liga aos serviços da Google quando é ativado.";
        /// <summary>O estado actual é um problema a mostrar ao jogador (erro do vídeo, rede).</summary>
        public bool StatusIsProblem { get; private set; }
        public bool Enabled => _browser != null && !_failed;
        public bool Failed => _failed;
        /// <summary>Volume efectivo enviado ao player (0–1), já com distância e oclusão — para a UI.</summary>
        public float SpatialVolume { get; private set; }
        /// <summary>Diagnóstico: atenuação (distância × paredes) da coluna mais audível e a distância a ela.</summary>
        public float LastAttenuation { get; private set; } = 1f;
        public float LastDistance { get; private set; }
        /// <summary>Diagnóstico vindo da página: volume do player (0–100), volume do &lt;video&gt; e loudness do vídeo (dB).</summary>
        public int PageVolume { get; private set; } = -1;
        public bool PageMuted { get; private set; }
        public float ElementVolume { get; private set; } = -1f;
        public float LoudnessDb { get; private set; } = 99f;
        public bool Occluded { get; private set; }
        public int PlayerState { get; private set; } = -1;
        /// <summary>O navegador bloqueou a reprodução com som: a HUD mostra "[J] ATIVAR SOM".</summary>
        public bool AutoplayBlocked { get; private set; }
        /// <summary>A página está a mostrar um anúncio do YouTube (a música começa a seguir).</summary>
        public bool AdShowing { get; private set; }
        /// <summary>O YouTube já deixa pular o anúncio: [K] carrega no botão "Pular" da página.</summary>
        public bool CanSkipAd => AdShowing && _hasSkip;
        /// <summary>O jogador silenciou este anúncio ([J]); o som volta sozinho quando a música começa.</summary>
        public bool AdMuted { get; private set; }
        /// <summary>A música deste jogador está pronta e a sala espera pelos anúncios/carregamentos dos outros.</summary>
        public bool WaitingForOthers { get; private set; }
        /// <summary>Código do erro do vídeo actual (0 = nenhum; ver YouTubeWatchPage).</summary>
        public int ErrorCode { get; private set; }
        public double PlayerTime { get; private set; }
        public string PlayerDiagnostic { get; private set; } = "";
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
            StatusIsProblem = false;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser != null) return;
            try
            {
                _controller = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Jukebox/watch.js"));
                // O jogador activou o player explicitamente: deixa o vídeo arrancar sem outro clique dentro da página
                // (sem isto o Chromium bloqueia a reprodução automática com som).
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--autoplay-policy=no-user-gesture-required");
                _browser = new GameObject("OfficialYouTubePage").AddComponent<WebViewObject>();
                _browser.transform.SetParent(transform, false);
                _browser.Init(cb: OnMessage, err: OnNavigationError, ld: OnLoaded, hooked: OnHooked);
                if (!_browser.IsInitialized())
                {
                    Fail("Player indisponível: instale/atualize o Microsoft Edge WebView2 Runtime — ou abre o vídeo no YouTube.");
                    return;
                }
                _browser.SetURLPattern("", "", YouTubeWatchPage.HookPattern);
                // Um frame de vídeo a cada 3 do jogo chega para o painel e poupa GPU/CPU (RT + upscalers).
                _browser.bitmapRefreshCycle = 3;
                _browser.drawTexture = false;
                _browser.captureTexture = false;
                _browser.SetSize(_pageW, _pageH);
                _browser.SetVisibility(false);
                _loadedVideo = "";
                Status = "A abrir a página oficial do YouTube…";
                Debug.Log("[Jukebox] navegador embutido pronto (página oficial do YouTube)");
            }
            catch (Exception e)
            {
                Fail("Player indisponível: instale/atualize o Microsoft Edge WebView2 Runtime. (" + e.GetType().Name + ")");
            }
#else
            Status = "O player integrado está disponível na versão Windows — abre o vídeo no YouTube.";
            StatusIsProblem = true;
            _failed = true;
#endif
        }

        /// <summary>Testes automáticos: o player deste jogador falha como se o WebView2 fechasse (a sala continua).</summary>
        public void SimulateFailure(string message) => Fail(message);

        void Fail(string message)
        {
            Status = message;
            StatusIsProblem = true;
            _failed = true;
            _pageAlive = false;
            Debug.LogWarning("[Jukebox] " + message);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser != null) Destroy(_browser.gameObject);
            _browser = null;
#endif
            var expander = (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand;
            if (YouTubeCatalog.PlaylistExpander == expander) YouTubeCatalog.PlaylistExpander = null;
        }

        // ================================================================== navegação

        /// <summary>Abre a página oficial do vídeo na posição da sala (um carregamento por faixa, nunca por sincronização).</summary>
        void Navigate(string video, double position, bool retry)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string url = YouTubeWatchPage.Url(video, position);
            if (url == null) return;
            if (!retry || _loadedVideo != video)
            {
                _loadAttempts = 0;
                _erroredVideo = _endedVideo = _readyVideo = "";
                ErrorCode = 0;
                _trackSince = Time.unscaledTime;
            }
            _loadAttempts++;
            _loadedVideo = video;
            _pageVideo = "";
            _pageAlive = _hasPlayerRect = false;
            _loadedAt = Time.unscaledTime;
            _retryAt = _mismatchSince = -1f;
            AdShowing = AutoplayBlocked = _hasSkip = false;
            _browser.LoadURL(url);
            if (!StatusIsProblem || !retry) { Status = "A abrir a página oficial do YouTube…"; StatusIsProblem = false; }
            Debug.Log("[Jukebox] página oficial: " + url + (retry ? " (nova tentativa " + _loadAttempts + ")" : ""));
#endif
        }

        void OnLoaded(string url)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Controlador do jogo instalado em cada documento novo (a navegação interna do YouTube mantém-no).
            if (_browser != null && !string.IsNullOrEmpty(_controller)) _browser.EvaluateJS(_controller);
#endif
        }

        void OnNavigationError(string error)
        {
            // Rede (offline, DNS, tempo esgotado): é local a este cliente — tenta outra vez sem mexer na fila da sala.
            Status = "Sem ligação ao YouTube — a tentar outra vez…";
            StatusIsProblem = true;
            _retryAt = Time.unscaledTime + Mathf.Min(30f, 5f * Mathf.Max(1, _loadAttempts));
            Debug.LogWarning("[Jukebox] a página oficial não carregou (" + error + ")");
        }

        /// <summary>
        /// Endereço fora do YouTube (login da Google, anúncio, outro site): nunca abre dentro do jogo. Se veio de um
        /// clique do jogador no painel, abre no navegador do sistema.
        /// </summary>
        void OnHooked(string url)
        {
            bool fromClick = Time.unscaledTime - _lastClickAt < ClickOpensFor;
            if (fromClick && YouTubeWatchPage.IsSafeExternal(url))
            {
                Application.OpenURL(url);
                Status = "Abri no teu navegador: " + (url.Length > 60 ? url.Substring(0, 59) + "…" : url);
                StatusIsProblem = false;
            }
            Debug.Log("[Jukebox] navegação fora do YouTube " + (fromClick ? "aberta no navegador" : "ignorada") + ": " + url);
        }

        void OnMessage(string json)
        {
            PlayerEvent e;
            try { e = JsonUtility.FromJson<PlayerEvent>(json); } catch (Exception) { return; }
            if (e == null) return;
            var net = NetGame.Instance;
            var current = net != null && net.HasSnapshot ? net.View.Music.Online.Current : null;
            bool isCurrent = current != null && e.video == current.VideoId;
            switch (e.type)
            {
                case "install":
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    if (Time.unscaledTime >= _nextInstall && _browser != null) { _nextInstall = Time.unscaledTime + 1f; _browser.EvaluateJS(_controller); }
#endif
                    break;
                case "page":
                    _lastPageAt = Time.unscaledTime;
                    _pageAlive = true;
                    _pageVideo = e.video ?? "";
                    _reportedW = e.vw;
                    _reportedH = e.vh;
                    // Só conta como player o rectângulo já com o layout final (a toda a largura da página).
                    _hasPlayerRect = e.rect != null && e.rect.Length == 4 && e.rect[2] >= e.vw * 0.6f && e.rect[3] > 0;
                    if (_hasPlayerRect) _playerRect = new RectInt(e.rect[0], e.rect[1], e.rect[2], e.rect[3]);
                    AdShowing = e.ad && _pageVideo == _loadedVideo;
                    _hasSkip = AdShowing && e.skip != null && e.skip.Length == 4 && e.skip[2] > 0;
                    if (_hasSkip) _skipRect = new RectInt(e.skip[0], e.skip[1], e.skip[2], e.skip[3]);
                    if (e.ready && _pageVideo == _loadedVideo) _readyVideo = _pageVideo;
                    // O som volta sozinho quando o anúncio que o jogador silenciou acaba.
                    if (!AdShowing && AdMuted) { AdMuted = false; _nextSync = 0f; }
                    if (_pageVideo == _loadedVideo && e.code < 100) { PlayerState = e.code; PlayerTime = e.time; }
                    // Durante um anúncio o player da página reporta o estado do anúncio (muitas vezes -1), não o da música.
                    if (_pageVideo == _loadedVideo && !StatusIsProblem && (AdShowing || PlayerState == 1 || WaitingForOthers))
                        Status = AdShowing ? "Anúncio do YouTube — a música começa a seguir, para todos ao mesmo tempo."
                            : WaitingForOthers ? "À espera dos outros jogadores (anúncios) — a música começa ao mesmo tempo para todos."
                            : "A tocar na página oficial do YouTube.";
                    PlayerDiagnostic = (_pageVideo.Length > 0 ? _pageVideo : "sem player") + " · estado " + e.code + " · " + e.time.ToString("0") + " s" + (AdShowing ? " · ANÚNCIO" : "");
                    if (_pageVideo == _loadedVideo && !AdShowing && (e.ev > 0.0 || e.pv > 0))
                    {
                        PageVolume = e.pv;
                        PageMuted = e.pm || e.em;
                        ElementVolume = (float)e.ev;
                        LoudnessDb = (float)e.ldb;
                    }
                    if (Time.unscaledTime >= _nextStateLog)
                    {
                        _nextStateLog = Time.unscaledTime + 10f;
                        Debug.Log("[Jukebox] página: " + PlayerDiagnostic + " · som: enviado " + Mathf.RoundToInt(_smoothedVolume * 100f) + " (geral " +
                                  _game.Settings.MasterVolume.ToString("0.00") + " × jukebox " + _game.Settings.JukeboxVolume.ToString("0.00") + " × distância " +
                                  LastAttenuation.ToString("0.00") + (Occluded ? " abafado" : "") + ", " + LastDistance.ToString("0.0") + " m) · player " + e.pv +
                                  (e.pm ? " MUDO" : "") + " · <video> " + e.ev.ToString("0.00") + (e.em ? " MUDO" : "") + " · loudness " + e.ldb.ToString("0.0") + " dB");
                    }
                    break;
                case "blocked":
                    if (!isCurrent) return;
                    AutoplayBlocked = true;
                    Status = "O navegador bloqueou a reprodução automática com som: carrega [J] (ou ▶ no player da jukebox).";
                    break;
                case "meta":
                    if (!isCurrent) return;
                    AutoplayBlocked = false;
                    StatusIsProblem = false;
                    Debug.Log("[Jukebox] a tocar " + e.video + " (" + e.duration.ToString("0") + " s) na página oficial");
                    // A página oficial dá a duração (e o título, quando o link entrou sem metadados).
                    if (current.Duration <= 0 || current.Title == OnlineTrack.PlaceholderTitle)
                        net.ReportOnlineMusicMeta(e.video, e.duration, e.title, e.author);
                    Status = "A tocar na página oficial do YouTube.";
                    break;
                case "error":
                    if (!isCurrent || _erroredVideo == e.video) return;
                    _erroredVideo = e.video;
                    ErrorCode = e.code;
                    Status = YouTubeWatchPage.Describe(e.code);
                    StatusIsProblem = true;
                    Debug.LogWarning("[Jukebox] " + e.video + " não toca na página oficial: código " + e.code + " (" + e.title + ")");
                    // Um aviso de conteúdo confirma-se no próprio player (clique no painel); o resto vai para o anfitrião.
                    if (e.code != YouTubeWatchPage.NeedsConfirmation) net.ReportOnlineMusicEnd(e.video, true, e.code);
                    break;
                case "ended":
                    if (!isCurrent) return;
                    _endedVideo = e.video;
                    net.ReportOnlineMusicEnd(e.video, false);
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

        /// <summary>[J] durante um anúncio: o jogador silencia-o (ou volta a ligar o som); o som volta sozinho com a música.</summary>
        public void ToggleAdMute()
        {
            if (!AdShowing) return;
            AdMuted = !AdMuted;
            if (AdMuted) _smoothedVolume = 0f;
            _nextSync = 0f;
        }

        /// <summary>
        /// [K]: o jogador carrega no botão "Pular" do próprio YouTube (só existe quando o YouTube já deixa pular). É o
        /// mesmo clique que faria no vídeo do painel — o jogo nunca pula anúncios sozinho.
        /// </summary>
        public void SkipAd()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!CanSkipAd || _browser == null || _reportedW <= 0 || _reportedH <= 0) return;
            int x = Mathf.RoundToInt((_skipRect.x + _skipRect.width * 0.5f) / _reportedW * (_pageW - 1));
            int y = Mathf.RoundToInt((_skipRect.y + _skipRect.height * 0.5f) / _reportedH * (_pageH - 1));
            _browser.SendPointer(x, y, 0);
            _browser.SendPointer(x, y, 1);
            _browser.SendPointer(x, y, 3);
            _hasSkip = false;
#endif
        }

        /// <summary>Alternativa sempre disponível: o vídeo actual no YouTube, no navegador do sistema, na posição da sala.</summary>
        public void OpenOnYouTube()
        {
            var net = NetGame.Instance;
            var state = net != null && net.HasSnapshot ? net.View.Music.Online : null;
            if (state?.Current == null) return;
            string url = YouTubeWatchPage.Url(state.Current.VideoId, state.Position(net.NetworkManager.ServerTime.Time));
            if (url == null) return;
            Application.OpenURL(url);
            Debug.Log("[Jukebox] aberto no navegador do sistema: " + url);
        }

        // ================================================================== playlists (anfitrião, sem chave da API)

        IEnumerator Expand(string list, int max, Action<List<string>, string> done)
        {
            if (_probe == null) _probe = gameObject.AddComponent<YouTubePlaylistProbe>();
            yield return _probe.Expand(list, max, done);
        }

        // ================================================================== ciclo

        void Update()
        {
            var net = NetGame.Instance;
            var state = net != null && net.HasSnapshot && net.IsSpawned ? net.View.Music.Online : null;
            // No anfitrião decide quem pode avançar a fila: o player dele conta enquanto a página responde ou ainda carrega.
            if (net != null) net.LocalOnlinePlayerActive = _browser != null && !_failed && (_pageAlive || Time.unscaledTime - _loadedAt < LoadTimeout);

            // Quem já aceitou o player numa sessão anterior não precisa de carregar outra vez no botão.
            if (_browser == null && !_failed && state?.Current != null && _game != null && _game.Settings.YouTubeConsent) EnablePlayer();

            // Só o anfitrião resolve playlists sem chave (é quem adiciona à fila).
            bool offerExpander = net != null && net.IsServer && _browser != null && !_failed;
            var expander = (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand;
            if (offerExpander) YouTubeCatalog.PlaylistExpander = expander;
            else if (YouTubeCatalog.PlaylistExpander == expander) YouTubeCatalog.PlaylistExpander = null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser == null) return;
            float now = Time.unscaledTime;
            bool playing = state?.Current != null;
            // A imagem da página só é capturada enquanto o painel da Jukebox a mostra (o HUD chama ShowIn em cada
            // frame em que a desenha). Fora do painel não tapa o jogo nem gasta GPU — o som continua.
            bool inPanel = playing && Time.frameCount - _panelFrame <= 2;
            // A TV da sala também pede a imagem enquanto está à vista (e perto) deste jogador.
            bool onTv = playing && Time.frameCount - _tvFrame <= 2;
            _browser.SetVisibility(playing);
            _browser.captureTexture = inPanel || onTv;
            if (inPanel || onTv) FitPage(Mathf.Max(inPanel ? _panelRect.width : 0f, onTv ? _tvPixels : 0f));

            ReportBusy(net, playing ? state.Current.VideoId : "", now);
            WaitingForOthers = playing && state.AdHold && _readyVideo == state.Current.VideoId && !AdShowing;

            if (!playing)
            {
                // Fila do YouTube vazia: pausa já; ao fim de 90 s liberta a página (memória) até à próxima música.
                if (_idleSince < 0f) _idleSince = now;
                if (_loadedVideo.Length > 0 && now - _idleSince > IdleUnloadSeconds)
                {
                    _browser.LoadURL("about:blank");
                    _loadedVideo = _pageVideo = "";
                    _pageAlive = _hasPlayerRect = false;
                }
                else if (now >= _nextSync)
                {
                    _nextSync = now + 0.25f;
                    _browser.EvaluateJS("window.fsPause && fsPause()");
                }
                return;
            }
            _idleSince = -1f;

            string want = state.Current.VideoId;
            double position = state.Position(net.NetworkManager.ServerTime.Time);
            if (_loadedVideo != want) { Navigate(want, position, false); return; }
            if (_retryAt > 0f && now >= _retryAt) { Navigate(want, position, true); return; }
            // Nada respondeu desde a navegação (rede presa): volta a tentar; uma página sem player (escolha de cookies)
            // responde e fica à espera do jogador.
            if (!_pageAlive && now - _loadedAt > LoadTimeout)
            {
                if (_loadAttempts < 3) Navigate(want, position, true);
                else { Status = "O YouTube não carregou — verifica a ligação ou abre o vídeo no YouTube."; StatusIsProblem = true; _loadedAt = now; _loadAttempts = 0; }
                return;
            }
            // A página foi para outro vídeo (reprodução automática do YouTube, clique numa sugestão): volta ao da sala.
            bool otherVideo = _pageAlive && _pageVideo.Length > 0 && _pageVideo != want && now - _lastPageAt < 1f && _endedVideo != want;
            if (otherVideo)
            {
                if (_mismatchSince < 0f) _mismatchSince = now;
                else if (now - _mismatchSince > 4f) { Navigate(want, position, true); return; }
            }
            else _mismatchSince = -1f;

            if (now < _nextSync) return;
            _nextSync = now + 0.25f;
            float target = TargetVolume();
            _smoothedVolume = _smoothedVolume < 0f ? target : Mathf.MoveTowards(_smoothedVolume, target, 0.25f);
            SpatialVolume = _smoothedVolume;
            var command = new PlayerCommand
            {
                video = want,
                position = position,
                paused = state.Paused,
                hold = state.AdHold,
                volume = Mathf.RoundToInt(_smoothedVolume * 100f)
            };
            _browser.EvaluateJS("window.fsSync ? fsSync(" + JsonUtility.ToJson(command) + ") : Unity.call('{\"type\":\"install\"}')");
#endif
        }

        /// <summary>
        /// Diz ao anfitrião se este jogador ainda não está pronto para a faixa (página a carregar ou anúncio do YouTube),
        /// para a sala começar a música para todos ao mesmo tempo. Só manda quando muda.
        /// </summary>
        void ReportBusy(NetGame net, string video, float now)
        {
            bool busy = video.Length > 0 && !_failed && _loadedVideo == video && _readyVideo != video && ErrorCode == 0 &&
                        now - _trackSince < (float)OnlineMusicState.AdHoldLimit;
            string report = busy ? video : "";
            if (report == _busyReported || net == null || !net.IsSpawned) return;
            _busyReported = report;
            net.ReportOnlineMusicBusy(video, busy);
        }

        /// <summary>Largura da página pela do painel (nitidez), em degraus para não estar sempre a mudar o layout.</summary>
        void FitPage(float panelPixels)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(0f, panelPixels - MinPageWidth) / PageStep);
            int width = Mathf.Min(MaxPageWidth, MinPageWidth + steps * PageStep);
            if (width == _pageW) return;
            _pageW = width;
            _pageH = Mathf.RoundToInt(width * PageAspect);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            _browser.SetSize(_pageW, _pageH);
#endif
        }

        /// <summary>
        /// "Áudio espacial" possível com um player externo, com a mesma curva da rádio da jukebox: cheio até 4 m, ~87 % a
        /// 10 m, nunca abaixo de 45 % dentro da sala, e um pouco abafado (×0,8) quando há paredes/objectos grandes entre a
        /// câmara e a coluna. As colunas são a jukebox e a TV da
        /// sala (vale a mais audível). Volume individual (0–100%) e mute são locais — não mudam o que os outros ouvem.
        /// </summary>
        float TargetVolume()
        {
            var settings = _game.Settings;
            if (settings.JukeboxMuted || (AdMuted && AdShowing)) return 0f;
            var cam = _game.Rig != null && _game.Rig.Camera != null ? _game.Rig.Camera.transform : null;
            float attenuation = Attenuation(cam, transform, transform.position + Vector3.up * 1.1f, out bool occluded, out float distance);
            foreach (var speaker in _speakers)
            {
                if (speaker.Root == null) continue;
                float a = Attenuation(cam, speaker.Root, speaker.Root.TransformPoint(speaker.Local), out bool o, out float d);
                if (a > attenuation) { attenuation = a; occluded = o; distance = d; }
            }
            LastDistance = distance;
            Occluded = occluded;
            LastAttenuation = attenuation;
            return Mathf.Clamp01(settings.MasterVolume * settings.JukeboxVolume * attenuation);
        }

        float Attenuation(Transform cam, Transform owner, Vector3 point, out bool occluded, out float distance)
        {
            distance = cam != null ? Vector3.Distance(cam.position, point) : 0f;
            occluded = cam != null && IsOccluded(cam.position, point, owner);
            return DistanceGain(distance, occluded, LegacyVolume);
        }

        /// <summary>Ganho (0–1) a <paramref name="distance"/> metros da coluna, com ou sem paredes pelo meio.</summary>
        public static float DistanceGain(float distance, bool occluded, bool legacy = false)
        {
            if (legacy) return LegacyFullDistance / Mathf.Max(LegacyFullDistance, distance) * (occluded ? LegacyOccluded : 1f);
            float rolloff = Mathf.Clamp01((SilentDistance - distance) / (SilentDistance - FullVolumeDistance));
            return (1f - SpatialShare + SpatialShare * rolloff) * (occluded ? OccludedFactor : 1f);
        }

        struct Speaker { public Transform Root; public Vector3 Local; }
        readonly List<Speaker> _speakers = new List<Speaker>();

        /// <summary>Outra coluna da sala (a TV): o som do player sai também dali, com o mesmo volume local.</summary>
        public void AddSpeaker(Transform root, Vector3 localPoint) => _speakers.Add(new Speaker { Root = root, Local = localPoint });

        bool IsOccluded(Vector3 from, Vector3 to, Transform owner)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.5f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / length, _hits, length - 0.6f, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = _hits[i].collider;
                if (c == null || c.transform.IsChildOf(owner) || c is CharacterController || c is CapsuleCollider) continue;
                // Só bloqueia o que é grande (paredes, balcão, máquinas); tacos e bolas não abafam a música.
                if (c.bounds.size.magnitude > 1.2f) return true;
            }
            return false;
        }

        // ================================================================== máscara (o painel desenha só o player)

        Rect _panelRect;
        int _panelFrame = -10, _tvFrame = -10;
        float _tvPixels;

        /// <summary>
        /// A TV da sala quer a imagem neste frame (está à vista). <paramref name="pagePixels"/> é a largura em píxeis
        /// pedida à página: a mínima normalmente, a do ecrã quando o jogador está a ver a TV de perto.
        /// </summary>
        public void RequestFrame(float pagePixels)
        {
            _tvFrame = Time.frameCount;
            _tvPixels = pagePixels;
        }

        /// <summary>Rectângulo do vídeo no painel da Jukebox, em píxeis de ecrã (origem no canto superior esquerdo).</summary>
        public void ShowIn(Rect screenRect)
        {
            _panelRect = screenRect;
            _panelFrame = Time.frameCount;
        }

        /// <summary>
        /// A página não tem player (escolha de cookies, aviso do YouTube): o painel mostra-a inteira e o jogador
        /// responde com o rato.
        /// </summary>
        public bool ShowsWholePage => _pageAlive && !_hasPlayerRect && _pageVideo.Length == 0;

        /// <summary>
        /// Zona a mostrar (o player oficial, ou a página inteira) em fracções da página (0–1, origem em cima). Em
        /// fracções porque a página mede-se em píxeis CSS e a textura/rato em píxeis físicos (escala do Windows).
        /// </summary>
        Rect PageCrop()
        {
            if (!_hasPlayerRect || _reportedW <= 0 || _reportedH <= 0) return new Rect(0f, 0f, 1f, 1f);
            float x = Mathf.Clamp01((float)_playerRect.x / _reportedW), y = Mathf.Clamp01((float)_playerRect.y / _reportedH);
            return new Rect(x, y, Mathf.Min(1f - x, (float)_playerRect.width / _reportedW), Mathf.Min(1f - y, (float)_playerRect.height / _reportedH));
        }

        /// <summary>
        /// Último fotograma recortado: textura, coordenadas UV (já viradas — a página vem de cima para baixo) e
        /// proporção da zona. Falso enquanto a página carrega.
        /// </summary>
        public bool TryGetFrame(out Texture texture, out Rect uv, out float aspect)
        {
            texture = null;
            uv = default;
            aspect = 16f / 9f;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var tex = _browser != null ? _browser.CurrentTexture : null;
            if (tex == null || !_pageAlive || (!_hasPlayerRect && !ShowsWholePage)) return false;
            var crop = PageCrop();
            uv = CropUv(crop);
            aspect = crop.width * tex.width / Mathf.Max(1f, crop.height * tex.height);
            texture = tex;
            return true;
#else
            return false;
#endif
        }

        /// <summary>UV de uma zona da página (fracções, origem em cima) numa textura com as linhas de cima para baixo.</summary>
        public static Rect CropUv(Rect crop) => new Rect(crop.x, crop.y + crop.height, crop.width, -crop.height);

        /// <summary>Rectângulo com a proporção dada, centrado dentro de outro (barras escuras em vez de cortar o player).</summary>
        public static Rect Fit(Rect area, float aspect)
        {
            if (aspect <= 0f) return area;
            float w = area.width, h = w / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            return new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// Rato no vídeo do painel (0–1 dentro da zona desenhada) → a mesma posição na página: permite saltar um
        /// anúncio quando o YouTube deixar, responder à escolha de cookies ou confirmar um aviso.
        /// </summary>
        public void Pointer(Vector2 normalized, int state, float wheel = 0f)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (_browser == null || !_pageAlive) return;
            var crop = PageCrop();
            int x = Mathf.RoundToInt((crop.x + Mathf.Clamp01(normalized.x) * crop.width) * (_pageW - 1));
            int y = Mathf.RoundToInt((crop.y + Mathf.Clamp01(normalized.y) * crop.height) * (_pageH - 1));
            if (state == 1) _lastClickAt = Time.unscaledTime;
            _browser.SendPointer(x, y, state, wheel);
#endif
        }

        void OnDestroy()
        {
            var expander = (Func<string, int, Action<List<string>, string>, IEnumerator>)Expand;
            if (YouTubeCatalog.PlaylistExpander == expander) YouTubeCatalog.PlaylistExpander = null;
            if (Instance == this) Instance = null;
        }
    }
}
