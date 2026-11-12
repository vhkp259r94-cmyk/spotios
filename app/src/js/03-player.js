/* Reproductor unificado: YouTube (reproductor oficial incrustado, controlado por la app) y vídeo/audio HTML5 (catálogo libre). */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const subs = {};
  const on = (evt, fn) => { (subs[evt] = subs[evt] || []).push(fn); };
  const emit = (evt, data) => (subs[evt] || []).forEach((fn) => { try { fn(data); } catch (e) { console.error(e); } });

  let yt = null, ytReady = false, ytLoading = null, pendingId = null;
  let mode = null;                     // 'yt' | 'html5' | null
  let volume = 80;
  let eq = { gains: new Array(10).fill(0), on: false };
  const st = { playing: false, time: 0, duration: 0 };
  const media = () => document.getElementById('html5');

  function setState(p) {
    Object.assign(st, p);
    emit('state', Object.assign({}, st));
  }

  // ---------- YouTube ----------
  function ensureYT() {
    if (ytLoading) return ytLoading;
    ytLoading = new Promise((resolve, reject) => {
      const create = () => {
        const vars = { autoplay: 1, playsinline: 1, rel: 0, modestbranding: 1 };
        if (/^https?:/.test(location.origin)) vars.origin = location.origin;
        yt = new YT.Player('yt', {
          width: '100%', height: '100%', playerVars: vars,
          events: {
            onReady: () => {
              ytReady = true;
              yt.setVolume(volume);
              if (pendingId) { yt.loadVideoById(pendingId); pendingId = null; }
              postEq();
              resolve();
            },
            onStateChange: (e) => {
              if (mode !== 'yt') return;
              if (e.data === 0) { setState({ playing: false }); emit('ended'); }
              else setState({ playing: e.data === 1 || e.data === 3 });
            },
            onError: (e) => { if (mode === 'yt') emit('error', { code: e.data }); },
          },
        });
      };
      if (window.YT && window.YT.Player) { create(); return; }
      window.onYouTubeIframeAPIReady = create;
      const s = document.createElement('script');
      s.src = 'https://www.youtube.com/iframe_api';
      s.onerror = () => { ytLoading = null; reject(new Error('No se pudo cargar el reproductor de YouTube')); };
      document.head.appendChild(s);
    });
    return ytLoading;
  }

  function ytFrame() { return document.querySelector('iframe#yt') || document.querySelector('#videoLayer iframe'); }

  // ---------- Ecualizador (eq.js se inyecta en el marco de YouTube; aquí solo se le envían órdenes) ----------
  function postEq() {
    const f = ytFrame();
    if (f && f.contentWindow) f.contentWindow.postMessage({ lsEq: { gains: eq.gains, on: eq.on } }, '*');
  }

  window.addEventListener('message', (e) => { if (e.data && e.data.lsEqStatus) emit('eqstatus', e.data.lsEqStatus); });

  function eqStatus() {
    return new Promise((resolve) => {
      const f = ytFrame();
      if (!f || !f.contentWindow) { resolve(null); return; }
      let done = false;
      const finish = (v) => { if (!done) { done = true; resolve(v); } };
      on('eqstatus', finish);
      f.contentWindow.postMessage({ lsEqStatusReq: true }, '*');
      setTimeout(() => finish(null), 900);
    });
  }

  // ---------- HTML5 ----------
  function bindMedia() {
    const m = media();
    m.addEventListener('play', () => mode === 'html5' && setState({ playing: true }));
    m.addEventListener('pause', () => mode === 'html5' && setState({ playing: false }));
    m.addEventListener('ended', () => { if (mode === 'html5') { setState({ playing: false }); emit('ended'); } });
    m.addEventListener('error', () => { if (mode === 'html5') emit('error', { code: 'media' }); });
    m.addEventListener('timeupdate', () => mode === 'html5' && setState({ time: m.currentTime, duration: Number.isFinite(m.duration) ? m.duration : 0 }));
  }

  setInterval(() => {
    if (document.hidden) return;                       // con la app en segundo plano no se actualiza la pantalla
    if (mode !== 'yt' || !ytReady || !yt || typeof yt.getCurrentTime !== 'function') return;
    const t = yt.getCurrentTime(), d = yt.getDuration();
    setState({ time: Number.isFinite(t) ? t : 0, duration: Number.isFinite(d) && d > 0 ? d : st.duration });
  }, 500);

  // ---------- API ----------
  LS.Player = {
    on,
    init() { bindMedia(); },
    state: () => Object.assign({}, st),
    mode: () => mode,

    async load(item, opts) {
      const audioOnly = !!(opts && opts.audioOnly);
      st.time = 0; st.duration = item.duration || 0; st.playing = true;
      if (item.src === 'yt') {
        const m = media(); m.pause(); m.removeAttribute('src');
        mode = 'yt';
        setState({});
        try { await ensureYT(); } catch (e) { emit('error', { code: 'api', message: e.message }); return; }
        // En solo audio se pide la imagen más pequeña (menos decodificación = menos calor y batería)
        // Vídeo: 720p (antes sin indicar, y tras escuchar música podía quedarse en la calidad mínima); solo audio: la más pequeña
        const quality = audioOnly ? 'tiny' : 'hd720';
        if (ytReady) yt.loadVideoById({ videoId: item.id, suggestedQuality: quality }); else pendingId = item.id;
        if (ytReady && typeof yt.setPlaybackQuality === 'function') yt.setPlaybackQuality(quality);
        postEq();
      } else {
        if (yt && ytReady && typeof yt.stopVideo === 'function') yt.stopVideo();
        mode = 'html5';
        const m = media();
        m.src = item.url; m.volume = volume / 100;
        setState({});
        m.play().catch((e) => emit('error', { code: 'media', message: e.message }));
      }
    },

    play() { if (mode === 'yt' && ytReady) yt.playVideo(); else if (mode === 'html5') media().play(); },
    pause() { if (mode === 'yt' && ytReady) yt.pauseVideo(); else if (mode === 'html5') media().pause(); },
    toggle() { st.playing ? LS.Player.pause() : LS.Player.play(); },
    seek(t) {
      if (mode === 'yt' && ytReady) yt.seekTo(t, true);
      else if (mode === 'html5') media().currentTime = t;
      setState({ time: t });
    },
    setVolume(v) {
      volume = Math.max(0, Math.min(100, v));
      if (yt && ytReady) yt.setVolume(volume);
      media().volume = volume / 100;
    },
    setEq(gains, enabled) { eq = { gains: gains.slice(), on: !!enabled }; postEq(); },
    eqStatus,
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
