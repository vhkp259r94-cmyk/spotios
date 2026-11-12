// Ecualizador de 10 bandas para el vídeo de YouTube incrustado (Web Audio API).
// Se inyecta en el marco (iframe) de YouTube: Windows con WebView2, iOS con WKUserScript y Android con
// addDocumentStartJavaScript. La app lo controla con window.__lsEq(gananciasDb, activado) o, desde la
// página, con postMessage({ lsEq: { gains, on } }). Un único archivo para todas las plataformas.
(() => {
  // Solo en YouTube: sobre medios de otros orígenes (sin CORS) el audio quedaría en silencio
  if (!/(^|\.)youtube(-nocookie)?\.com$/.test(location.hostname)) return;
  // Segundo plano: el reproductor de YouTube se pausa solo cuando la página pasa a «oculta» (pantalla bloqueada o app en segundo
  // plano). Aquí se le indica que sigue visible y se descartan los avisos de cambio de visibilidad, como hace la
  // «reproducción en segundo plano» de Brave. Solo afecta al marco de YouTube; la interfaz de Nsound no se toca.
  if (!window.__lsKeepAlive) {
    window.__lsKeepAlive = true;
    try {
      const fixed = (proto, name, value) => { try { Object.defineProperty(proto, name, { configurable: true, get: () => value }); } catch (e) { /* ya definido */ } };
      fixed(Document.prototype, 'hidden', false);
      fixed(Document.prototype, 'webkitHidden', false);
      fixed(Document.prototype, 'visibilityState', 'visible');
      fixed(Document.prototype, 'webkitVisibilityState', 'visible');
      const swallow = (e) => e.stopImmediatePropagation();
      ['visibilitychange', 'webkitvisibilitychange'].forEach((t) => { document.addEventListener(t, swallow, true); window.addEventListener(t, swallow, true); });
      document.hasFocus = () => true;
    } catch (e) { /* si el navegador no lo permite, queda como antes */ }
  }
  if (window.__lsEqInstalled) return;
  window.__lsEqInstalled = true;

  const freqs = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
  let ctx = null, filters = [], analyser = null, attached = false;
  let gains = new Array(10).fill(0), enabled = false;

  function apply() {
    if (!attached) return;
    filters.forEach((f, i) => { f.gain.value = enabled ? gains[i] : 0; });
  }

  // Solo se engancha al <video> cuando el usuario activa el ecualizador: así no se toca el audio de otro modo.
  function attach(video) {
    if (attached) return;
    try {
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      const source = ctx.createMediaElementSource(video);
      filters = freqs.map((hz, i) => {
        const f = ctx.createBiquadFilter();
        f.type = i === 0 ? 'lowshelf' : (i === freqs.length - 1 ? 'highshelf' : 'peaking');
        f.frequency.value = hz;
        f.Q.value = 1.1;
        f.gain.value = 0;
        return f;
      });
      let node = source;
      filters.forEach(f => { node.connect(f); node = f; });
      analyser = ctx.createAnalyser();
      analyser.fftSize = 256;
      node.connect(analyser);
      analyser.connect(ctx.destination);
      attached = true;
      apply();
      if (ctx.state === 'suspended') ctx.resume();
    } catch (e) {
      window.__lsEqError = String(e);
    }
  }

  function level() {
    if (!analyser) return 0;
    const d = new Uint8Array(analyser.frequencyBinCount);
    analyser.getByteFrequencyData(d);
    return d.reduce((a, b) => a + b, 0) / d.length;
  }

  window.__lsEq = (g, on) => {
    gains = Array.isArray(g) ? g.map(Number) : gains;
    enabled = !!on;
    if (enabled && !attached) {
      const v = document.querySelector('video');
      if (v) attach(v);
    }
    apply();
  };

  window.__lsEqStatus = () => JSON.stringify({
    attached, state: ctx ? ctx.state : null, level: level(), error: window.__lsEqError || null
  });

  // Órdenes desde la página que contiene el iframe (iOS y Android)
  window.addEventListener('message', (e) => {
    const d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.lsEq) window.__lsEq(d.lsEq.gains, d.lsEq.on);
    if (d.lsEqStatusReq && e.source) e.source.postMessage({ lsEqStatus: JSON.parse(window.__lsEqStatus()) }, '*');
  });

  // Con el ecualizador activo ya hay un analizador: se envía el nivel a la página (10 veces por segundo, solo si suena)
  setInterval(() => {
    if (!attached || !ctx || ctx.state !== 'running' || window.parent === window) return;
    const v = document.querySelector('video');
    if (!v || v.paused) return;
    try { window.parent.postMessage({ lsLevel: Math.min(1, level() / 255) }, '*'); } catch (e) { /* sin página padre */ }
  }, 100);

  setInterval(() => {
    if (enabled && !attached) {
      const v = document.querySelector('video');
      if (v) attach(v);
    }
    if (enabled && ctx && ctx.state === 'suspended') ctx.resume();
  }, 1000);
})();
