/* Puente con la app nativa (iOS: WKWebView, Android: WebView). En un navegador usa el servidor de desarrollo. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const w = typeof window !== 'undefined' ? window : {};
  let platform = 'web';
  if (w.webkit && w.webkit.messageHandlers && w.webkit.messageHandlers.native) platform = 'ios';
  else if (w.AndroidBridge) platform = 'android';

  const pending = new Map();
  const listeners = [];
  let seq = 0;

  // Respuestas y eventos que envía la app nativa (Android responde por aquí; iOS usa promesas nativas)
  w.__nativeResolve = (id, ok, payload) => {
    const p = pending.get(id);
    if (!p) return;
    pending.delete(id);
    if (ok) p.resolve(payload); else p.reject(new Error(String(payload)));
  };
  w.__nativeEvent = (evt) => listeners.forEach((fn) => { try { fn(evt); } catch (e) { /* un oyente roto no debe afectar a los demás */ } });

  async function webCall(name, payload) {
    if (name === 'fetchText') {
      const r = await fetch('/__proxy?url=' + encodeURIComponent(payload.url));
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.text();
    }
    if (name === 'postJson') {
      const r = await fetch('/__post?url=' + encodeURIComponent(payload.url), { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: payload.body });
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.text();
    }
    throw new Error('Solo disponible en la app móvil');
  }

  function call(name, payload) {
    payload = payload || {};
    if (platform === 'ios') return w.webkit.messageHandlers.native.postMessage({ name, payload });
    if (platform === 'android') {
      return new Promise((resolve, reject) => {
        const id = ++seq;
        pending.set(id, { resolve, reject });
        try { w.AndroidBridge.call(id, name, JSON.stringify(payload)); } catch (e) { pending.delete(id); reject(e); }
      });
    }
    return webCall(name, payload);
  }

  LS.Native = {
    platform,
    call,
    onEvent: (fn) => listeners.push(fn),
    fetchText: (url) => call('fetchText', { url }),
    postJson: (url, body) => call('postJson', { url, body }),
    /** Las descargas al dispositivo solo existen en las apps móviles. */
    canDownload: platform !== 'web',
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
