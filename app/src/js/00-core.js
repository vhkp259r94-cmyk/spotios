/* Núcleo compartido (iOS, Android y navegador). Sin dependencias del DOM: se prueba con Node.
   Es un puerto de LibreStream.Core (C#): mismas reglas de filtro, mismas letras sincronizadas. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  // ---------- Datos fijos ----------
  LS.GENRES = ['Éxitos', 'Reggaetón', 'Pop', 'Rock', 'Electrónica', 'Hip hop', 'Latino', 'Lo-fi'];
  LS.EQ_LABELS = ['31', '62', '125', '250', '500', '1k', '2k', '4k', '8k', '16k'];
  LS.EQ_PRESETS = {
    'Plano': [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    'Graves': [6, 5, 4, 2, 0, 0, 0, 0, 0, 0],
    'Agudos': [0, 0, 0, 0, 0, 1, 3, 5, 6, 7],
    'Voz': [-2, -2, -1, 1, 3, 4, 3, 1, 0, -1],
    'Rock': [5, 4, 2, -1, -2, -1, 2, 4, 5, 6],
    'Pop': [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2],
    'Hip hop': [6, 5, 2, 3, -1, -1, 2, 0, 2, 3],
    'Electrónica': [6, 5, 2, 0, -2, 2, 1, 2, 5, 6],
    'Clásica': [4, 3, 2, 1, -1, -1, 0, 2, 3, 4],
  };
  LS.EQ_CUSTOM = 'Personalizado';
  LS.SHELVES = [
    { title: 'Eventos del año', queries: ['La Velada del Año', 'Kings League resumen'] },
    { title: 'Series y tráilers', queries: ['tráiler oficial serie 2026', 'series estreno tráiler'] },
    { title: 'YouTubers', queries: ['Ibai', 'AuronPlay', 'TheGrefg'] },
    { title: 'Deporte', queries: ['resumen partido goles'] },
    { title: 'Documentales', queries: ['documental completo en español'] },
    { title: 'Humor y entretenimiento', queries: ['monólogo humor', 'programa entretenimiento'] },
  ];

  // ---------- Utilidades ----------
  LS.fold = function (s) {
    return String(s).toLowerCase().normalize('NFKD').replace(/[̀-ͯ]/g, '')
      .replace(/[^\p{L}\p{N}\s]/gu, ' ').replace(/\s+/g, ' ').trim();
  };

  LS.formatTime = function (sec) {
    sec = Number.isFinite(sec) ? Math.max(0, Math.floor(sec)) : 0;
    const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
  };

  /** "3:25" → 205, "1:02:03" → 3723; nulo o no numérico (directos) → 0. */
  LS.parseLength = function (text) {
    if (!text || !String(text).trim()) return 0;
    let total = 0;
    for (const part of String(text).split(':')) {
      if (!/^\d+$/.test(part)) return 0;
      total = total * 60 + parseInt(part, 10);
    }
    return total;
  };

  LS.isValidVideoId = (id) => typeof id === 'string' && /^[A-Za-z0-9_-]{11}$/.test(id);

  // ---------- YouTube: URL y análisis de resultados ----------
  // sp=EgQIAxAB → "Esta semana" + "Vídeo"; sp=EgIQAQ%3D%3D → solo "Vídeo"
  LS.searchUrl = function (query, opts) {
    opts = opts || {};
    let url = 'https://www.youtube.com/results?search_query=' + encodeURIComponent(String(query).trim());
    if (opts.thisWeek) url += '&sp=EgQIAxAB';
    else if (opts.videosOnly) url += '&sp=EgIQAQ%3D%3D';
    return url;
  };

  function text(t) {
    if (!t || typeof t !== 'object') return '';
    if (typeof t.simpleText === 'string') return t.simpleText;
    return (Array.isArray(t.runs) && t.runs[0] && typeof t.runs[0].text === 'string') ? t.runs[0].text : '';
  }

  function readVideo(v) {
    if (!v || typeof v !== 'object' || !LS.isValidVideoId(v.videoId)) return null;
    const title = text(v.title);
    if (!title) return null;
    return {
      id: v.videoId,
      title,
      channel: text(v.ownerText) || text(v.longBylineText),
      duration: LS.parseLength(v.lengthText && v.lengthText.simpleText),
      published: text(v.publishedTimeText) || null,
    };
  }

  LS.parseYouTube = function (html) {
    const m = /var ytInitialData = (\{.*?\});<\/script>/s.exec(String(html || ''));
    if (!m) return [];
    let data;
    try { data = JSON.parse(m[1]); } catch (e) { return []; }
    const out = [], seen = new Set();
    (function walk(o, depth) {
      if (depth > 40 || o === null || typeof o !== 'object') return;
      if (Array.isArray(o)) { for (const x of o) walk(x, depth + 1); return; }
      for (const k of Object.keys(o)) {
        if (k === 'videoRenderer') {
          const v = readVideo(o[k]);
          if (v && !seen.has(v.id)) { seen.add(v.id); out.push(v); }
        } else walk(o[k], depth + 1);
      }
    })(data, 0);
    return out;
  };

  // ---------- Filtro música / vídeo ----------
  // JS no tiene \b con acentos: se delimita con "no letra ni número" a cada lado.
  const NOT_A_SONG = /(^|[^\p{L}\p{N}_])(tutorial|lesson|lecci[oó]n|reaction|reacci[oó]n|karaoke|how to|c[oó]mo tocar|review|podcast|interview|entrevista|nightcore|slowed|sped up|8d audio|full album|[aá]lbum completo|playlist|mix|megamix|compilation|recopilaci[oó]n)(?![\p{L}\p{N}_])|\d+\s*(hours?|horas?)/iu;
  const AUDIO_ONLY = /(^|[^\p{L}\p{N}_])(audio|lyrics?|letra|visuali[sz]er|topic|full album|[aá]lbum completo)(?![\p{L}\p{N}_])/iu;

  LS.isLive = (v) => !(v.duration > 0);
  LS.isSong = (v) => !LS.isLive(v) && v.duration > 60 && v.duration <= 720 && !NOT_A_SONG.test(v.title);
  LS.isVideo = (v) => !LS.isLive(v) && !AUDIO_ONLY.test(v.title);

  /** Mezcla resultados de varias búsquedas alternándolos; solo vídeos de verdad (≥ 45 s), sin repetidos. */
  LS.mergeShelf = function (lists, take) {
    const merged = [], most = Math.max(0, ...lists.map((l) => l.length));
    for (let i = 0; i < most; i++) for (const l of lists) if (i < l.length) merged.push(l[i]);
    const seen = new Set();
    return merged.filter((v) => LS.isVideo(v) && v.duration >= 45 && !seen.has(v.id) && seen.add(v.id)).slice(0, take);
  };

  // ---------- Títulos (puerto de MetadataMatcher) ----------
  const NOISE = /\s*[\(\[\{][^\)\]\}]*?\b(official|oficial|video|vídeo|audio|lyrics?|letra|visualizer|hd|hq|4k|1080p|720p|explicit|clean|mv|m\/v|remaster(?:ed)?|\d{4}\s+remaster(?:ed)?)\b[^\)\]\}]*[\)\]\}]/gi;
  const NOISE_TAIL = /\s+[-–|]\s+(official\s+)?(music\s+)?(video|audio|lyrics?|visualizer)\s*$/i;
  const REMASTER_TAIL = /\s+[-–]\s+(\d{4}\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$/i;
  const FEAT = /\s*[\(\[]?\b(?:feat\.?|ft\.?|featuring)\s+([^\)\]]+?)[\)\]]?(?=\s*[\(\[\-–]|$)/i;
  const VERSION_GROUP = /\s*[\(\[]([^\)\]]*)[\)\]]|\s+[-–]\s+([^\-–]+)$/g;
  const VERSIONS = [
    ['remix', /\b(remix|rmx|mix|bootleg|rework)\b/i],
    ['live', /\b(live|en vivo|en directo|unplugged)\b/i],
    ['acoustic', /\b(acoustic|acústic[oa])\b/i],
    ['instrumental', /\b(instrumental|karaoke)\b/i],
    ['cover', /\bcover\b/i],
    ['edit', /\b(radio edit|edit|extended)\b/i],
  ];

  LS.cleanTitle = function (raw) {
    let t = String(raw).replace(NOISE, '').replace(NOISE_TAIL, '').replace(REMASTER_TAIL, '');
    let featuring = [];
    const m = FEAT.exec(t);
    if (m) {
      featuring = m[1].split(/,|&| y | and /i).map((s) => s.trim()).filter(Boolean);
      t = t.slice(0, m.index) + t.slice(m.index + m[0].length);
    }
    let version = 'original';
    for (const g of t.matchAll(VERSION_GROUP)) {
      const chunk = g[1] !== undefined ? g[1] : (g[2] || '');
      const hit = VERSIONS.find((v) => v[1].test(chunk));
      if (!hit) continue;
      version = hit[0];
      t = t.replace(g[0], '');
      break;
    }
    t = t.replace(/\s+/g, ' ').replace(/^[ \-–|]+|[ \-–|]+$/g, '');
    return { title: t, featuring, version };
  };

  /** Deduce artista y título de un vídeo de YouTube ("Artista - Canción (Official Video)", canal "ArtistaVEVO"). */
  LS.guessTrack = function (videoTitle, channel) {
    const clean = LS.cleanTitle(videoTitle).title;
    const artist = String(channel || '').replace(/\s*(-\s*Topic|VEVO|Official|Oficial)\s*$/i, '').trim();
    for (const sep of [' - ', ' – ', ' — ']) {
      const i = clean.indexOf(sep);
      if (i > 0) return { artist: clean.slice(0, i).trim(), title: clean.slice(i + sep.length).trim() };
    }
    return { artist, title: clean };
  };

  // ---------- Letras (LRC) ----------
  /** "[01:02.50] línea" → líneas ordenadas por tiempo; una línea con varias marcas se repite. */
  LS.parseLrc = function (lrc) {
    const lines = [];
    if (!lrc || !String(lrc).trim()) return lines;
    const tag = /\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]/g;
    for (const raw of String(lrc).split('\n')) {
      const tags = [...raw.matchAll(tag)];
      if (!tags.length) continue;
      const txt = raw.replace(tag, '').trim();
      for (const t of tags) {
        const frac = t[3] ? parseFloat('0.' + t[3]) : 0;
        lines.push({ time: parseInt(t[1], 10) * 60 + parseInt(t[2], 10) + frac, text: txt });
      }
    }
    return lines.sort((a, b) => a.time - b.time);
  };

  /** Índice de la línea que suena en `position` (−1 si aún no ha empezado). */
  LS.lyricIndexAt = function (lines, position) {
    let index = -1;
    for (let i = 0; i < lines.length && lines[i].time <= position; i++) index = i;
    return index;
  };

  /** Elige el mejor resultado de LRCLIB: primero sincronizado, luego duración más cercana. */
  LS.pickLyrics = function (json, durationSec) {
    let arr;
    try { arr = typeof json === 'string' ? JSON.parse(json) : json; } catch (e) { return null; }
    if (!Array.isArray(arr)) return null;
    let best = null, bestScore = Infinity;
    for (const e of arr) {
      if (!e || typeof e !== 'object') continue;
      const synced = LS.parseLrc(e.syncedLyrics);
      const plain = typeof e.plainLyrics === 'string' ? e.plainLyrics : '';
      if (!synced.length && !plain.trim()) continue;
      const d = typeof e.duration === 'number' ? e.duration : 0;
      const diff = durationSec > 0 && d > 0 ? Math.abs(d - durationSec) : 30;
      const score = diff + (synced.length ? 0 : 1000);
      if (score >= bestScore) continue;
      bestScore = score;
      best = synced.length ? { synced: true, lines: synced }
        : { synced: false, lines: plain.split('\n').map((l) => ({ time: -1, text: l.trim() })) };
    }
    return best;
  };

  // ---------- Ecualizador ----------
  LS.eqMatch = function (bands) {
    for (const [name, p] of Object.entries(LS.EQ_PRESETS)) if (p.every((v, i) => v === bands[i])) return name;
    return LS.EQ_CUSTOM;
  };

  // ---------- Reglas de anuncios (las usan los envoltorios nativos y se prueban aquí) ----------
  LS.AD_HOSTS = ['doubleclick.net', 'googlesyndication.com', 'googleadservices.com', 'googletagservices.com',
    'googletagmanager.com', 'google-analytics.com', 'adservice.google.com', '2mdn.net', 'imasdk.googleapis.com',
    'moatads.com', 'scorecardresearch.com', 'adsrvr.org', 'amazon-adsystem.com'];
  LS.AD_PATHS = ['/pagead/', '/api/stats/ads', '/get_midroll_info', '/ptracking', '/youtubei/v1/player/ad_break'];

  LS.isAdUrl = function (url) {
    let u;
    try { u = new URL(url); } catch (e) { return false; }
    const host = u.hostname.toLowerCase();
    if (LS.AD_HOSTS.some((h) => host === h || host.endsWith('.' + h))) return true;
    const yt = host === 'youtube.com' || host.endsWith('.youtube.com') || host.endsWith('.youtube-nocookie.com');
    return yt && LS.AD_PATHS.some((p) => u.pathname.toLowerCase().startsWith(p));
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
