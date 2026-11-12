/* Importar una canción, una lista pública o un artista desde otro servicio.
   Solo se leen datos públicos (título y artista); después cada canción se busca en YouTube Music.
   No se inicia sesión, no se copia audio, portadas ni letras. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  LS.IMPORT_MAX = 100;
  LS.IMPORT_HOSTS = ['open.spotify.com', 'api.deezer.com', 'music.apple.com', 'soundcloud.com'];   // además de www.youtube.com
  LS.IMPORT_NOTICE = 'Solo se pueden importar canciones, listas públicas o artistas. No accedemos a tu cuenta ni copiamos contenido: '
    + 'solo leemos los títulos públicos y los buscamos en YouTube. Puede fallar si el servicio cambia su página.';
  LS.IMPORT_HINT = 'Pega un enlace de YouTube, SoundCloud, Spotify, Deezer o Apple Music';

  const ID = /^[A-Za-z0-9_-]{6,64}$/;
  const clean = (s) => String(s == null ? '' : s).replace(/\s+/g, ' ').trim();
  const ok = (service, kind, id, url) => ({ service, kind, id, fetchUrl: url });
  const no = (error) => ({ error });

  /** Enlace pegado → { service, kind: 'track'|'playlist'|'artist', id, fetchUrl } o { error }. */
  LS.parseImportLink = function (text) {
    let u;
    try { u = new URL(clean(text)); } catch (e) { return no('Eso no parece un enlace. Pega la dirección completa (https://…).'); }
    if (u.protocol !== 'https:' && u.protocol !== 'http:') return no('Eso no parece un enlace web.');
    const host = u.hostname.toLowerCase().replace(/^(www|m)\./, '');
    const seg = u.pathname.split('/').filter(Boolean);

    if (host === 'youtube.com' || host === 'music.youtube.com' || host === 'youtu.be') {
      const list = u.searchParams.get('list');
      if (host !== 'youtu.be' && seg[0] === 'playlist' && list && ID.test(list)) return ok('youtube', 'playlist', list, 'https://www.youtube.com/playlist?list=' + list);
      const v = host === 'youtu.be' ? seg[0] : (seg[0] === 'watch' ? u.searchParams.get('v') : (seg[0] === 'shorts' ? seg[1] : null));
      if (v && /^[A-Za-z0-9_-]{11}$/.test(v)) {
        return ok('youtube', 'track', v, 'https://www.youtube.com/oembed?format=json&url=' + encodeURIComponent('https://www.youtube.com/watch?v=' + v));
      }
      return no('De YouTube solo se pueden importar vídeos sueltos o listas de reproducción (no canales ni mixes).');
    }
    if (host === 'open.spotify.com') {
      const s = seg[0] && seg[0].startsWith('intl-') ? seg.slice(1) : seg;
      const kind = { track: 'track', playlist: 'playlist', artist: 'artist' }[s[0]];
      if (!kind) return no('De Spotify solo se pueden importar canciones, listas y artistas (no álbumes ni podcasts).');
      if (!s[1] || !/^[A-Za-z0-9]{10,32}$/.test(s[1])) return no('No reconozco ese enlace de Spotify.');
      return ok('spotify', kind, s[1], `https://open.spotify.com/embed/${s[0]}/${s[1]}`);
    }
    if (host === 'deezer.com' || host === 'deezer.page.link') {
      if (host === 'deezer.page.link') return no('Los enlaces cortos de Deezer no se pueden leer: abre la lista y copia la dirección completa.');
      const i = seg.findIndex((x) => x === 'track' || x === 'playlist' || x === 'artist');
      if (i < 0) return no('De Deezer solo se pueden importar canciones, listas y artistas (no álbumes).');
      const id = seg[i + 1];
      if (!/^\d+$/.test(id || '')) return no('No reconozco ese enlace de Deezer.');
      const kind = seg[i];
      return ok('deezer', kind, id, kind === 'artist' ? `https://api.deezer.com/artist/${id}/top?limit=${LS.IMPORT_MAX}` : `https://api.deezer.com/${kind}/${id}`);
    }
    if (host === 'music.apple.com') {
      const kind = seg.includes('playlist') ? 'playlist' : seg.includes('artist') ? 'artist' : (seg.includes('song') || u.searchParams.get('i')) ? 'track' : null;
      if (!kind) return no('De Apple Music solo se pueden importar canciones, listas y artistas (no álbumes).');
      return ok('apple', kind, seg[seg.length - 1] || '', 'https://music.apple.com' + u.pathname + (kind === 'track' && u.searchParams.get('i') ? '?i=' + encodeURIComponent(u.searchParams.get('i')) : ''));
    }
    if (host === 'soundcloud.com') {
      if (seg.length === 2 && !['sets', 'tracks', 'likes', 'reposts', 'albums', 'popular-tracks'].includes(seg[1])) {
        return ok('soundcloud', 'track', seg.join('/'), 'https://soundcloud.com/oembed?format=json&url=' + encodeURIComponent('https://soundcloud.com/' + seg.join('/')));
      }
      return no('De SoundCloud por ahora solo se pueden importar canciones sueltas (no listas ni artistas).');
    }
    if (host === 'amazon.com' || host === 'tidal.com') return no('Ese servicio no es compatible.');
    return no('Servicio no compatible. Usa YouTube, SoundCloud, Spotify, Deezer o Apple Music.');
  };

  const track = (title, artist) => ({ title: clean(title), artist: clean(artist) });
  const valid = (t) => t.title && t.title.length <= 200;

  function nextData(html) {
    const m = /<script id="__NEXT_DATA__" type="application\/json">(.*?)<\/script>/s.exec(html);
    if (!m) return null;
    try { return JSON.parse(m[1]).props.pageProps.state.data.entity || null; } catch (e) { return null; }
  }

  function youtubePlaylist(html) {
    const m = /var ytInitialData = (\{.*?\});<\/script>/s.exec(html);
    if (!m) return null;
    let data; try { data = JSON.parse(m[1]); } catch (e) { return null; }
    const out = []; let name = '';
    const text = (x) => (x && (x.simpleText || (x.runs || []).map((r) => r.text).join(''))) || '';
    (function walk(o, d) {
      if (d > 40 || o === null || typeof o !== 'object') return;
      if (Array.isArray(o)) { for (const x of o) walk(x, d + 1); return; }
      for (const k of Object.keys(o)) {
        if (k === 'playlistVideoRenderer') out.push(track(text(o[k].title), text(o[k].shortBylineText).replace(/ - Topic$/, '')));
        else if (k === 'playlistHeaderRenderer' && !name) name = text(o[k].title);
        else walk(o[k], d + 1);
      }
    })(data, 0);
    return { name, tracks: out };
  }

  function appleLd(html) {
    const out = []; let name = '';
    for (const m of html.matchAll(/<script[^>]*ld\+json[^>]*>(.*?)<\/script>/gs)) {
      let d; try { d = JSON.parse(m[1]); } catch (e) { continue; }
      const by = (x) => clean(Array.isArray(x) ? (x[0] || {}).name : x && x.name);
      if (d['@type'] === 'MusicRecording') { out.push(track(d.name, by(d.byArtist))); name = d.name; }
      else if (Array.isArray(d.track)) { name = d.name || name; for (const t of d.track) out.push(track(t.name, by(t.byArtist) || by(d.byArtist))); }
      else if (d['@type'] === 'MusicGroup' || d['@type'] === 'Person') { name = d.name || name; }
    }
    return { name, tracks: out };
  }

  /** Respuesta del servicio → { name, tracks: [{title, artist}], artistName? }. Si no se entiende, tracks queda vacío. */
  LS.parseImported = function (link, text) {
    const raw = String(text || '');
    let r = { name: '', tracks: [] };
    try {
      if (link.service === 'youtube') {
        if (link.kind === 'track') { const j = JSON.parse(raw); r = { name: j.title, tracks: [track(j.title, String(j.author_name || '').replace(/ - Topic$/, ''))] }; }
        else r = youtubePlaylist(raw) || r;
      } else if (link.service === 'spotify') {
        const e = nextData(raw);
        if (e) {
          if (link.kind === 'track') r = { name: e.name, tracks: [track(e.title || e.name, (e.artists || []).map((a) => a.name).join(', '))] };
          else r = { name: e.name || e.title, tracks: (e.trackList || []).map((t) => track(t.title, t.subtitle)), artistName: link.kind === 'artist' ? e.name : '' };
        }
      } else if (link.service === 'deezer') {
        const j = JSON.parse(raw);
        if (link.kind === 'track') r = { name: j.title, tracks: [track(j.title, j.artist && j.artist.name)] };
        else if (link.kind === 'playlist') r = { name: j.title, tracks: ((j.tracks && j.tracks.data) || []).map((t) => track(t.title, t.artist && t.artist.name)) };
        else r = { name: ((j.data || [])[0] || { artist: {} }).artist.name || 'Artista', tracks: (j.data || []).map((t) => track(t.title, t.artist && t.artist.name)) };
        if (link.kind === 'artist') r.artistName = r.name;
      } else if (link.service === 'apple') {
        r = appleLd(raw);
      } else if (link.service === 'soundcloud') {
        const j = JSON.parse(raw);                         // título «Canción by Artista»
        const m = /^(.*) by (.+)$/s.exec(clean(j.title));
        r = { name: j.title, tracks: [m ? track(m[1], m[2]) : track(j.title, j.author_name)] };
      }
    } catch (e) { r = { name: '', tracks: [] }; }
    const seen = new Set();
    r.tracks = (r.tracks || []).filter(valid).filter((t) => { const k = (t.title + '|' + t.artist).toLowerCase(); return !seen.has(k) && seen.add(k); }).slice(0, LS.IMPORT_MAX);
    r.name = clean(r.name).slice(0, 60);
    return r;
  };

  /** Elige la mejor canción de los resultados de YouTube Music para una pista importada, o null si ninguna encaja. */
  LS.pickImportMatch = function (wanted, results) {
    const norm = (s) => clean(s).toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9 ]/g, ' ').replace(/\s+/g, ' ').trim();
    const wt = norm(wanted.title).replace(/\b(feat|ft|with)\b.*$/, '').trim(), wa = norm(wanted.artist).split(/ and | y |,/)[0].trim();
    for (const m of results || []) {
      const t = norm(m.title), a = norm(m.channel || m.artist || '');
      const titleOk = t === wt || t.includes(wt) || wt.includes(t);
      const artistOk = !wa || a.includes(wa) || wa.includes(a.split(' ')[0]);
      if (titleOk && artistOk) return m;
    }
    return null;
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
