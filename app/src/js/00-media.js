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

  /** Posición válida para setPositionState, o null si todavía no se puede dar. */
  LS.mediaPosition = function (time, duration) {
    const d = Number(duration), t = Number(time);
    if (!(d > 0) || !isFinite(d) || !isFinite(t)) return null;
    return { duration: d, position: Math.min(Math.max(t, 0), d), playbackRate: 1 };
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
