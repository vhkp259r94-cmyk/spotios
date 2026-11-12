/* Datos para la pantalla de bloqueo y los controles del sistema (Media Session en navegador; MPNowPlayingInfoCenter en iPhone). */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const clean = (s) => String(s == null ? '' : s).replace(/\s+/g, ' ').trim();

  /** Canción actual → { title, artist, album, artwork: [{ src, sizes, type }] } con el artista y la carátula limpios. */
  LS.mediaMeta = function (it) {
    if (!it) return { title: '', artist: '', album: '', artwork: [] };
    const parts = clean(it.sub).split(' · ').map((x) => x.trim()).filter(Boolean);
    const artist = clean(it.artist).replace(/ - Topic$/, '') || parts[0] || '';
    const album = it.src === 'yt' && parts.length >= 2 && parts[0] === artist ? parts[1].replace(/ · YouTube$/, '') : '';
    const src = it.thumb && /^https:\/\//.test(it.thumb) ? it.thumb : '';
    const square = /googleusercontent\.com|ggpht\.com|=w\d+-h\d+/.test(src);          // carátula de YouTube Music: cuadrada
    return {
      title: clean(it.title), artist, album,
      artwork: src ? [{ src, sizes: square ? '544x544' : '320x180', type: 'image/jpeg' }] : [],
    };
  };

  /**
   * Color dominante de una carátula a partir de sus píxeles (RGBA). Pondera lo saturado y claro, ignora grises y negros
   * (las barras laterales de algunas miniaturas). Devuelve { h (0-359 o null si es gris), s, l } ya limitados para un fondo agradable.
   */
  LS.coverColor = function (px) {
    let w = 0, x = 0, y = 0, sat = 0, lig = 0;
    for (let i = 0; i + 3 < px.length; i += 4) {
      if (px[i + 3] < 128) continue;
      const r = px[i] / 255, g = px[i + 1] / 255, b = px[i + 2] / 255;
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
      if (d < 0.12 || mx < 0.2) continue;                                   // gris u oscuro: no cuenta
      const s = d / mx;
      const wt = s * s * (0.35 + mx);
      const h = (mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4) * 60 * Math.PI / 180;
      x += Math.cos(h) * wt; y += Math.sin(h) * wt; sat += s * wt; lig += ((mx + mn) / 2) * wt; w += wt;
    }
    if (w <= 0) return { h: null, s: 10, l: 30 };
    const hue = Math.round((Math.atan2(y, x) * 180 / Math.PI + 360) % 360);
    const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
    return { h: hue, s: Math.round(clamp(sat / w * 100, 40, 85)), l: Math.round(clamp(22 + (lig / w) * 26, 28, 44)) };
  };

  /** Intensidad 0..1 de un «pulso» sin audio real: ritmo suave y estable por canción (para el efecto de Reproduciendo). */
  LS.pulseAt = function (seconds, seed) {
    const f = 1.5 + (Math.abs(seed | 0) % 10) / 12;                          // 1,5–2,3 pulsos por segundo
    const beat = Math.pow(0.5 + 0.5 * Math.sin(seconds * Math.PI * 2 * f), 3);
    const swell = 0.5 + 0.5 * Math.sin(seconds * 0.9 + (seed % 7));
    return Math.max(0, Math.min(1, 0.65 * beat + 0.35 * swell));
  };

  /**
   * ¿Hay que reanudar sola la música tras una pausa? Solo si la hizo el sistema al bloquear la pantalla: la página está oculta,
   * la pausa llega en los 4 s siguientes a ocultarse, no la pidió el usuario (ni los cascos), no terminó la canción y quedan reintentos.
   */
  LS.shouldAutoResume = function (c) {
    if (c.playing || c.userPause || !c.hasNow || (c.tries || 0) >= 3) return false;
    if (c.duration && c.time >= c.duration - 2) return false;
    if (c.hidden) return c.now - (c.hiddenAt || 0) <= 4000;                          // el sistema pausa justo al bloquear
    return !!c.wasPlaying && c.now - (c.visibleAt || 0) <= 4000;                     // o justo al desbloquear, si sonaba antes
  };

  /** Posición válida para setPositionState, o null si todavía no se puede dar. */
  LS.mediaPosition = function (time, duration) {
    const d = Number(duration), t = Number(time);
    if (!(d > 0) || !isFinite(d) || !isFinite(t)) return null;
    return { duration: d, position: Math.min(Math.max(t, 0), d), playbackRate: 1 };
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
