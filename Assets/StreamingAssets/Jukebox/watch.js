// Controlador do jogo dentro da página oficial do YouTube (youtube.com/watch). O jogo instala-o depois de cada
// carregamento e manda comandos (vídeo, posição, pausa, volume) 4× por segundo; a resposta "page" diz onde está o
// player na página (o jogo recorta só essa zona — a página não é alterada) e o estado da reprodução.
// Só usa a API do próprio player da página (movie_player). Anúncios correm como no site: durante um anúncio não há
// saltos de posição nem silêncio forçado; o jogador pode saltá-lo no próprio player quando o YouTube deixar.
(() => {
  if (window.fsSync) return;
  const post = o => Unity.call(JSON.stringify(o));
  // Estados de playabilityStatus que impedem a reprodução aqui → códigos que o jogo entende.
  const codes = { LOGIN_REQUIRED: 1001, AGE_CHECK_REQUIRED: 1001, AGE_VERIFICATION_REQUIRED: 1001, UNPLAYABLE: 1002, LIVE_STREAM_OFFLINE: 1002, ERROR: 100, CONTENT_CHECK_REQUIRED: 1003 };
  // Botão "Pular" do player (escondido com display:none até o YouTube deixar pular).
  const SKIP = '.ytp-skip-ad-button, .ytp-ad-skip-button, .ytp-ad-skip-button-modern';
  let target = '', lastSeek = 0, reported = '', erred = '', ended = '', blocked = false, wantSince = 0;
  // Quanto a página demora a retomar depois de um seekTo (aprendido): a correção salta essa margem à frente, senão
  // um vídeo pesado volta a ficar ~2 s atrás a cada salto (medido na TV com 4K/60).
  let seekLead = 0, checkAt = 0;

  const player = () => { const p = document.getElementById('movie_player'); return p && p.getPlayerState ? p : null; };
  const isAd = p => p.classList.contains('ad-showing');
  function pageVideo(p) {
    try { const d = p.getVideoData(); if (d && d.video_id) return d.video_id; } catch (e) { }
    return new URLSearchParams(location.search).get('v') || '';
  }
  // Resposta do player para o vídeo pedido. Vídeos que exigem login não trazem videoDetails: vale então o ID da página.
  function playability(p) {
    const mine = (r, id) => r && r.playabilityStatus && (r.videoDetails ? r.videoDetails.videoId === target : id === target);
    let r = null;
    try { r = p.getPlayerResponse ? p.getPlayerResponse() : null; } catch (e) { }
    if (mine(r, pageVideo(p))) return r.playabilityStatus;
    r = window.ytInitialPlayerResponse;
    return mine(r, new URLSearchParams(location.search).get('v')) ? r.playabilityStatus : null;
  }
  function rect(p) {
    const r = p.getBoundingClientRect();
    return r.width >= 160 && r.height >= 90 ? [r.left, r.top, r.width, r.height].map(Math.round) : null;
  }

  // Botão "Pular" do próprio YouTube, só quando já se pode carregar nele (o jogador carrega com [K]).
  function skipButton(p) {
    const b = p.querySelector(SKIP);
    if (!b) return null;
    const r = b.getBoundingClientRect(), st = getComputedStyle(b);
    if (r.width < 8 || r.height < 8 || st.display === 'none' || st.visibility === 'hidden' || +st.opacity < 0.5 || b.disabled) return null;
    return [r.left, r.top, r.width, r.height].map(Math.round);
  }

  window.fsPause = () => { const p = player(); if (p && p.getPlayerState() === 1) p.pauseVideo(); };
  // Acção explícita do jogador (tecla J / botão) depois de um bloqueio de autoplay: volta a pedir a reprodução e, se
  // o som continuar recusado, arranca sem som (sempre permitido); o volume volta no próximo fsSync.
  window.fsUnblock = () => {
    blocked = false; wantSince = 0;
    const p = player(); if (!p) return;
    try { p.playVideo(); } catch (e) { }
    setTimeout(() => { if (p.getPlayerState() !== 1) { p.mute(); p.playVideo(); } }, 400);
  };

  window.fsSync = s => {
    if (target !== s.video) { target = s.video; blocked = false; wantSince = 0; checkAt = 0; }
    // ready: a música (não um anúncio) está carregada e pode tocar — a sala espera até todos estarem prontos.
    const out = { type: 'page', video: '', rect: null, ad: false, ready: false, skip: null, code: -1, time: 0, vw: innerWidth, vh: innerHeight };
    const p = player();
    // Sem player: ainda a carregar, ou outra página do YouTube (escolha de cookies, aviso) — o jogo mostra-a inteira.
    if (!p) { post(out); return; }
    // Com player, o jogo mostra o topo da página (o player): nada de deslocar a página para baixo.
    if (window.scrollY > 0) window.scrollTo(0, 0);
    out.video = pageVideo(p);
    out.rect = rect(p);
    out.ad = isAd(p);
    const ps = playability(p);
    if (ps && ps.status && ps.status !== 'OK') {
      out.code = codes[ps.status] || 1002;
      if (erred !== target) { erred = target; post({ type: 'error', video: target, code: out.code, title: String(ps.reason || '').slice(0, 120) }); }
      post(out);
      return;
    }
    // A página está noutro vídeo (reprodução automática do YouTube ou clique): fica parada até o jogo a recarregar.
    if (out.video !== target) {
      if (p.getPlayerState() === 1 && !out.ad) p.pauseVideo();
      post(out);
      return;
    }

    p.setVolume(s.volume);
    if (s.volume <= 0) { if (!p.isMuted()) p.mute(); } else if (p.isMuted()) p.unMute();
    // Diagnóstico do som (só leitura): volume do player, volume real do <video> (já com a normalização de
    // loudness do YouTube) e o loudness do vídeo.
    try {
      const v = p.querySelector('video');
      out.pv = p.getVolume(); out.pm = p.isMuted(); out.ev = v ? v.volume : -1; out.em = v ? v.muted : false;
      const r = p.getPlayerResponse ? p.getPlayerResponse() : null;
      const a = r && r.playerConfig && r.playerConfig.audioConfig;
      out.ldb = a && typeof a.loudnessDb === 'number' ? a.loudnessDb : 99;
    } catch (e) { }
    const state = p.getPlayerState();
    const now = performance.now();
    if (out.ad) {
      out.skip = skipButton(p);
      // Anúncio: só pausa/retoma com a sala; posição e duração ficam para o vídeo.
      if (s.paused) { if (state === 1) p.pauseVideo(); }
      else if (state === 2 || state === -1 || state === 5) p.playVideo();
      out.code = state;
      post(out);
      return;
    }
    const duration = p.getDuration ? p.getDuration() : 0;
    const time = p.getCurrentTime ? p.getCurrentTime() : 0;
    out.time = time;
    out.code = state;
    out.ready = state === 1 || state === 2 || state === 5;
    if (s.hold) {
      // A sala espera pelos anúncios dos outros: a música fica parada no início até todos estarem prontos.
      if (state === 1 || state === 3) p.pauseVideo();
      if (Math.abs(time - s.position) > 1) p.seekTo(s.position, true);
      wantSince = 0;
      post(out);
      return;
    }
    // Já acabou aqui e o anfitrião ainda não passou à seguinte: fica parado (sem recomeçar do início).
    const finishedHere = state === 0 && (duration <= 0 || s.position > duration - 3);
    if (state === 0 && ended !== target) { ended = target; post({ type: 'ended', video: target }); }
    if (checkAt && now >= checkAt && state === 1 && !s.paused) {
      // 3 s depois de uma correção: o que ainda falta passa a fazer parte da margem (0–3 s).
      seekLead = Math.min(3, Math.max(0, seekLead + (s.position - time)));
      checkAt = 0;
    }
    if (!blocked && !finishedHere && now - lastSeek > 6000 && Math.abs(time - s.position) > 1 && !(duration > 0 && s.position >= duration - 1)) {
      // Correcção de desvio: só quando passa de 1 s, no máximo a cada 6 s (sem saltos constantes nem "reinícios").
      p.seekTo(s.position + (s.paused ? 0 : seekLead), true);
      lastSeek = now;
      checkAt = s.paused ? 0 : now + 3000;
    }
    if (s.paused) { if (state === 1 || state === 3) p.pauseVideo(); wantSince = 0; }
    else if (!blocked && !finishedHere && state !== 1 && state !== 3) {
      p.playVideo();
      if (!wantSince) wantSince = now;
      // Pediu-se "play" há 4 s e continua parado: o navegador recusou reproduzir com som.
      if (now - wantSince > 4000) { blocked = true; post({ type: 'blocked', video: target }); }
    } else if (state === 1) wantSince = 0;
    if (state === 1 && reported !== target && duration > 0) {
      reported = target;
      let v = {};
      try { v = p.getVideoData() || {}; } catch (e) { }
      post({ type: 'meta', video: target, duration, title: String(v.title || '').slice(0, 120), author: String(v.author || '').slice(0, 60) });
    }
    post(out);
  };
})();
