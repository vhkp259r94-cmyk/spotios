/* Iconos propios (SVG): los símbolos de texto como ⏮ ⏸ ⬇ ⚙ se dibujan como emojis de colores en iOS y parecen pegatinas. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const S = 'fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"';
  const F = 'fill="currentColor" stroke="none"';
  const PATHS = {
    prev: [F, '<path d="M6 5h2.4v14H6zM20 5.5v13L9.5 12z"/>'],
    next: [F, '<path d="M15.6 5H18v14h-2.4zM4 5.5v13L14.5 12z"/>'],
    play: [F, '<path d="M7 4.8v14.4a.8.8 0 0 0 1.2.7l11.4-7.2a.8.8 0 0 0 0-1.4L8.2 4.1A.8.8 0 0 0 7 4.8z"/>'],
    pause: [F, '<rect x="6" y="5" width="4.2" height="14" rx="1.2"/><rect x="13.8" y="5" width="4.2" height="14" rx="1.2"/>'],
    heart: [S, '<path d="M12 20s-7.5-4.6-7.5-10.2A4.4 4.4 0 0 1 12 7.2a4.4 4.4 0 0 1 7.5 2.6C19.5 15.4 12 20 12 20z"/>'],
    heartOn: ['fill="currentColor" stroke="currentColor" stroke-width="2" stroke-linejoin="round"', '<path d="M12 20s-7.5-4.6-7.5-10.2A4.4 4.4 0 0 1 12 7.2a4.4 4.4 0 0 1 7.5 2.6C19.5 15.4 12 20 12 20z"/>'],
    plus: [S, '<path d="M12 5v14M5 12h14"/>'],
    download: [S, '<path d="M12 4v11m0 0-4-4m4 4 4-4M5 19.5h14"/>'],
    video: [S, '<rect x="3" y="6" width="18" height="12" rx="2.5"/>'],
    chevDown: [S, '<path d="M6 9.5l6 6 6-6"/>'],
    close: [S, '<path d="M6 6l12 12M18 6 6 18"/>'],
    up: [S, '<path d="M6 14.5l6-6 6 6"/>'],
    down: [S, '<path d="M6 9.5l6 6 6-6"/>'],
    import: [S, '<path d="M12 4v11m0 0-4-4m4 4 4-4M5 19.5h14"/>'],
    home: [S, '<path d="M4 11l8-7 8 7v8.5a.5.5 0 0 1-.5.5H15v-6H9v6H4.5a.5.5 0 0 1-.5-.5z"/>'],
    music: [S, '<path d="M9 18V6.5l10-2V16"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="16.5" cy="16" r="2.5"/>'],
    videos: [S, '<rect x="3" y="5" width="18" height="14" rx="3"/><path d="M10.2 9.2v5.6l4.6-2.8z" fill="currentColor"/>'],
    library: [S, '<path d="M4 6.5h16M4 12h16M4 17.5h9"/>'],
    settings: [S, '<path d="M4 7h9M19 7h1M4 17h1M11 17h9"/><circle cx="16" cy="7" r="2.5"/><circle cx="8" cy="17" r="2.5"/>'],
  };

  LS.icon = (name) => {
    const p = PATHS[name];
    return p ? `<svg class="ic" viewBox="0 0 24 24" ${p[0]} aria-hidden="true" focusable="false">${p[1]}</svg>` : '';
  };
  LS.setIcon = (el, name) => { if (el && el.dataset.icon !== name) { el.dataset.icon = name; el.innerHTML = LS.icon(name); } };
  /** Rellena los elementos con data-ic="nombre" (los del HTML estático). */
  LS.paintIcons = (root) => (root || document).querySelectorAll('[data-ic]').forEach((el) => LS.setIcon(el, el.dataset.ic));
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
