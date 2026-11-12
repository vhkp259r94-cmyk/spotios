/* Interfaz de LibreStream (compartida por iOS, Android y navegador). */
(function () {
  'use strict';
  const { Player, Native } = LS;

  const CATALOG_URL = 'https://vhkp259r94-cmyk.github.io/spotios/catalog.json';
  const $ = (sel, root) => (root || document).querySelector(sel);
  const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  function safeStorage() {
    try { const s = window.localStorage; s.setItem('ls.t', '1'); s.removeItem('ls.t'); return s; }
    catch (e) { const m = {}; return { getItem: (k) => (k in m ? m[k] : null), setItem: (k, v) => { m[k] = String(v); } }; }
  }
  const store = LS.createStores(safeStorage());

  // Estilo de la interfaz: «clasico» sin efectos; el resto activa body.fx y la paleta (data-theme)
  const darkMq = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  function applyLook(id) {
    const chosen = id;
    if (id === 'auto') id = darkMq && !darkMq.matches ? 'claro' : 'nsound';       // sigue al sistema: claro de día, oscuro de noche
    applyLook.chosen = chosen;
    const look = LS.LOOKS.find((l) => l[0] === id) || LS.LOOKS[0];
    document.body.classList.toggle('fx', look[0] !== 'clasico');
    if (look[0] === 'clasico') delete document.body.dataset.theme; else document.body.dataset.theme = look[0];
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', look[2]);
    document.documentElement.style.backgroundColor = look[2];
  }

  if (darkMq && darkMq.addEventListener) darkMq.addEventListener('change', () => { if (applyLook.chosen === 'auto') applyLook('auto'); });

  // ---------- Estado ----------
  LS.paintIcons();
  const S = {
    page: 'home', sub: 'saved',             // sub: saved | downloads | pl:<id>
    scope: 'all',                           // filtro de Inicio: all | track | video
    genre: LS.GENRES[0], query: '',
    releases: [], heroIndex: 0,
    shelves: null, shelvesLoading: false,
    catalog: [],
    queue: [], index: -1, now: null, audioOnly: false,
    lyrics: null, lyricsFor: '', lyricIndex: -1,
    renderId: 0, playing: false, time: 0, duration: 0, skips: 0,
  };
  const reg = new Map();     // clave → elemento (para las tarjetas)
  const lists = new Map();   // lista → claves (la cola al pulsar una tarjeta)
  const cache = new Map();   // búsquedas ya hechas

  const remember = (it) => { it.key = it.src + ':' + it.id; reg.set(it.key, it); return it; };
  const ytItem = (v) => remember({
    src: 'yt', id: v.id, title: v.title, sub: v.channel, duration: v.duration, published: v.published,
    thumb: `https://i.ytimg.com/vi/${v.id}/mqdefault.jpg`, kind: 'video',
  });
  const catItem = (c) => remember({
    src: 'cat', id: c.id, title: c.title, sub: c.artist, duration: c.durationSec, thumb: c.artworkUrl || '',
    url: (c.sources.find((s) => s.provider !== 'youtube') || c.sources[0]).url, kind: c.kind, license: c.license && c.license.spdx,
  });
  const savedItem = (s) => remember(Object.assign({}, s));

  function toast(msg, ms) {
    const t = $('#toast');
    t.textContent = msg; t.classList.add('show');
    clearTimeout(toast.t);
    toast.t = setTimeout(() => t.classList.remove('show'), ms || 4500);
  }

  const persistent = LS.createCache(safeStorage(), { ttl: 30 * 60 * 1000, max: 24 });
  let running = 0;
  const waiting = [];
  const slot = () => new Promise((res) => { if (running < 3) { running++; res(); } else waiting.push(res); });
  const release = () => { const n = waiting.shift(); if (n) n(); else running--; };

  // Música: solo canciones de YouTube Music (versiones de audio: sin intros, charla ni vídeos con efectos)
  async function musicSearch(query) {
    const k = 'musica|' + query;
    if (cache.has(k)) return cache.get(k);
    const saved = persistent.get(k);
    if (saved) { cache.set(k, saved); return saved; }
    await slot();
    try {
      const r = LS.parseYtMusic(await Native.postJson(LS.MUSIC_SEARCH_URL, LS.musicSearchBody(query)));
      if (!r.length) throw new Error('YouTube Music no devolvió canciones');
      cache.set(k, r);
      persistent.set(k, r);
      return r;
    } finally { release(); }
  }

  async function ytSearch(query, opts) {
    const k = JSON.stringify([query, opts || {}]);
    if (cache.has(k)) return cache.get(k);
    const saved = persistent.get(k);
    if (saved) { cache.set(k, saved); return saved; }
    await slot();                                      // como mucho 3 descargas a la vez (cada página pesa 1-2 MB)
    try {
      const html = await Native.fetchText(LS.searchUrl(query, opts));
      const r = LS.parseYouTube(html);
      if (!r.length) throw new Error('YouTube no devolvió vídeos: ' + LS.diagnoseYouTube(html));   // un vacío nunca se guarda en caché
      cache.set(k, r);
      persistent.set(k, r);
      return r;
    } finally { release(); }
  }

  const musicItem = (m) => remember({
    src: 'yt', id: m.id, title: m.title, artist: m.channel, sub: m.channel + (m.album ? ' · ' + m.album : ''), duration: m.duration,
    thumb: m.art || `https://i.ytimg.com/vi/${m.id}/mqdefault.jpg`, kind: 'track',
  });

  const hue = (id) => { let h = 17; for (const c of String(id)) h = (h * 31 + c.charCodeAt(0)) | 0; return Math.abs(h) % 360; };

  // ---------- Tarjetas ----------
  function card(it, listId, shape, idx) {
    return `<button class="card ${shape}${S.now && S.now.key === it.key ? ' cur' : ''}" style="--i:${Math.min(idx || 0, 12)}" data-act="play" data-key="${esc(it.key)}" data-list="${esc(listId)}">
      <div class="thumb" style="--h:${hue(it.id)}">${it.thumb ? `<img loading="lazy" alt="" src="${esc(it.thumb)}" onerror="this.remove()">` : ''}
      ${it.duration > 0 ? `<span class="dur">${LS.formatTime(it.duration)}</span>` : (it.src === 'yt' ? '<span class="dur">EN DIRECTO</span>' : '')}<span class="eqmini" aria-hidden="true"><i></i><i></i><i></i></span><span class="play" aria-hidden="true">${LS.icon('play')}</span></div>
      <div class="t">${esc(it.title)}</div><div class="s">${esc(it.sub)}</div></button>`;
  }

  function setList(id, items) { lists.set(id, items.map((i) => i.key)); return items; }
  const grid = (id, items, shape) => `<div class="grid ${shape}">${items.map((i, n) => card(i, id, shape, n)).join('')}</div>`;
  const skeleton = (n) => `<div class="grid video">${Array.from({ length: n || 8 }, () => '<div class="skel"></div>').join('')}</div>`;


  // ---------- Últimas escuchadas (solo en este dispositivo) ----------
  const recentStore = safeStorage();
  function recentList() { try { const a = JSON.parse(recentStore.getItem('ls.recent') || '[]'); return Array.isArray(a) ? a.filter((x) => x && x.id && x.src) : []; } catch (e) { return []; } }
  function recentPush(it) {
    if (!it || it.src === 'cat') return;
    const slim = { src: it.src, id: it.id, title: it.title, artist: it.artist, sub: it.sub, duration: it.duration, thumb: it.thumb, kind: it.kind };
    try { recentStore.setItem('ls.recent', JSON.stringify([slim, ...recentList().filter((x) => x.src + ':' + x.id !== it.src + ':' + it.id)].slice(0, 12))); } catch (e) { /* sin almacenamiento */ }
  }

  // Color de la carátula (si el navegador deja leerla); si no, un tono estable según la canción
  function artHue(url, id, apply) {
    apply(hue(id));
    if (!url) return;
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => {
      try {
        const c = document.createElement('canvas'); c.width = c.height = 16;
        const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0, 16, 16);
        const d = g.getImageData(0, 0, 16, 16).data; let best = 0, bh = null;
        for (let i = 0; i < d.length; i += 4) {
          const r = d[i] / 255, gr = d[i + 1] / 255, b = d[i + 2] / 255, mx = Math.max(r, gr, b), mn = Math.min(r, gr, b), sat = mx ? (mx - mn) / mx : 0;
          if (sat * mx > best && mx - mn > 0.08) {
            best = sat * mx;
            const dl = mx - mn;
            let h = mx === r ? ((gr - b) / dl) % 6 : mx === gr ? (b - r) / dl + 2 : (r - gr) / dl + 4;
            bh = Math.round((h * 60 + 360) % 360);
          }
        }
        if (bh !== null) apply(bh);
      } catch (e) { /* imagen de otro origen sin permiso: se queda el tono por defecto */ }
    };
    img.src = url;
  }

  // ---------- Navegación ----------
  const PAGES = ['home', 'music', 'videos', 'library', 'settings'];
  function route() {
    const parts = (location.hash.replace(/^#\/?/, '') || 'home').split('/');
    S.page = PAGES.includes(parts[0]) ? parts[0] : 'home';
    S.sub = S.page === 'library' ? (parts[1] === 'downloads' ? 'downloads' : parts[1] === 'pl' && parts[2] ? 'pl:' + parts[2] : 'saved') : 'saved';
    $$('#nav [data-page]').forEach((b) => b.classList.toggle('on', b.dataset.page === S.page));
    render();
  }
  const go = (hash) => { if (location.hash === hash) route(); else location.hash = hash; };

  // El apartado decide qué se muestra: en Música solo música, en Vídeos solo vídeos; en Inicio, el filtro.
  const scopeNow = () => (S.page === 'music' ? 'track' : S.page === 'videos' ? 'video' : S.scope);

  function render() {
    const id = ++S.renderId;
    const v = $('#view');
    v.classList.remove('in'); void v.offsetWidth; v.classList.add('in');
    $('#main').scrollTop = 0;
    if (S.page === 'home') return renderHome(v, id);
    if (S.page === 'music') return renderMusic(v, id);
    if (S.page === 'videos') return renderVideos(v, id);
    if (S.page === 'library') return renderLibrary(v, id);
    return renderSettings(v);
  }

  function chips(items, current, attr) {
    return `<div class="chips">${items.map(([val, label]) => `<button class="chip ${val === current ? 'on' : ''}" data-${attr}="${esc(val)}">${esc(label)}</button>`).join('')}</div>`;
  }

  // ---------- Inicio ----------
  async function renderHome(v, id) {
    const q = S.query, sc = S.scope;
    const hr = new Date().getHours(), greet = hr < 6 ? 'Buenas noches' : hr < 13 ? 'Buenos días' : hr < 21 ? 'Buenas tardes' : 'Buenas noches';
    v.innerHTML = `<h1>${greet}</h1>${chips([['all', 'Todo'], ['track', 'Música'], ['video', 'Vídeos']], sc, 'scope')}
      ${q ? `<p class="hint">${sc === 'track' ? 'Canciones' : sc === 'video' ? 'Vídeos' : 'Resultados'} para «${esc(q)}».</p>` : ''}
      <section id="hero" class="hero" hidden></section>
      <section><h2 id="hitsTitle">${q ? 'Resultados en YouTube' : sc === 'track' ? 'Canciones del momento' : sc === 'video' ? 'Videoclips del momento' : 'Éxitos del momento'}</h2>
      <div id="hits">${skeleton()}</div></section>
      <section id="recentRow" hidden><h2>Seguir escuchando</h2><div id="recentGrid"></div></section>
      <section id="savedRow" hidden><h2>De tu biblioteca</h2><div id="savedGrid"></div></section>`;
    if (!q) { renderHero(); setTimeout(() => loadReleases(id), 1500); }
    const recent = recentList().map(savedItem);
    if (!q && recent.length) { $('#recentRow').hidden = false; $('#recentGrid').innerHTML = `<div class="shelf">${setList('home-recent', recent) && recent.map((i, n) => card(i, 'home-recent', 'video', n)).join('')}</div>`; }
    const saved = store.library.items().slice(0, 8).map(savedItem);
    if (!q && saved.length) { $('#savedRow').hidden = false; $('#savedGrid').innerHTML = grid(setList('home-saved', saved) && 'home-saved', saved, 'video'); }
    await showResults('#hits', id, 'home');
  }

  async function showResults(target, id, listId) {
    const sc = scopeNow(), q = S.query.trim();
    const box = $(target);
    try {
      let list, items;
      if (q.length === 1) { box.innerHTML = '<p class="hint">Escribe al menos dos letras.</p>'; return; }
      if (sc === 'track') {
        try {
          const songs = await musicSearch(q || LS.genreQuery(S.page === 'music' ? S.genre : LS.GENRES[0], new Date().getFullYear()));
          items = songs.filter((m) => LS.isSong({ title: m.title, duration: m.duration })).map(musicItem);
          if (!items.length) items = null;
        } catch (e) { items = null; }   // si YouTube Music falla, se usa la búsqueda normal
      }
      if (items) list = null;
      else if (q) list = await ytSearch(sc === 'track' ? q + ' audio' : q, { videosOnly: true });
      else if (S.page === 'music' && S.genre !== LS.GENRES[0]) list = await ytSearch(S.genre + ' official music video', { videosOnly: true });
      else list = await ytSearch('official music video ' + new Date().getFullYear(), { videosOnly: true });
      if (id !== S.renderId) return;
      if (!items) {
        list = sc === 'track' ? list.filter(LS.isSong) : sc === 'video' ? list.filter(LS.isVideo) : (q ? list.filter((x) => !LS.isLive(x)) : list.filter(LS.isSong));
        items = list.map(ytItem);
      }
      items = setList(listId, items);
      box.innerHTML = items.length ? grid(listId, items, sc === 'track' ? 'track' : 'video') : '<p class="hint">Sin resultados en YouTube.</p>';
    } catch (e) {
      if (id === S.renderId) box.innerHTML = `<p class="hint err">No se pudo buscar en YouTube: ${esc(e.message)}</p>`;
    }
  }

  // ---------- Anuncio de lanzamientos ----------
  async function loadReleases(id) {
    if (S.releases.length) return;
    try {
      const [latin, world] = await Promise.all([
        ytSearch('nuevo lanzamiento oficial video musical', { thisWeek: true }),
        ytSearch('new music official video', { thisWeek: true }),
      ]);
      const mixed = [];
      for (let i = 0; i < Math.max(latin.length, world.length); i++) { if (world[i]) mixed.push(world[i]); if (latin[i]) mixed.push(latin[i]); }
      const seen = new Set();
      S.releases = mixed.filter((x) => LS.isSong(x) && x.duration <= 480 && !seen.has(x.id) && seen.add(x.id)).slice(0, 6).map(ytItem);
      setList('releases', S.releases);
      if (id === S.renderId) renderHero();
    } catch (e) { /* sin conexión: no se muestra el anuncio */ }
  }

  function renderHero() {
    const el = $('#hero');
    if (!el || S.page !== 'home' || S.query || !S.releases.length) return;
    const it = S.releases[S.heroIndex % S.releases.length];
    const g = LS.guessTrack(it.title, it.sub);
    el.hidden = false;
    el.style.setProperty('--h', hue(it.id));
    el.innerHTML = `<img alt="" src="https://i.ytimg.com/vi/${esc(it.id)}/${innerWidth < 800 ? 'hqdefault' : 'maxresdefault'}.jpg" onerror="this.onerror=null;this.src='https://i.ytimg.com/vi/${esc(it.id)}/hqdefault.jpg'">
      <div class="shade"></div><div class="txt"><div class="label">NUEVO LANZAMIENTO${it.published ? ' · ' + esc(it.published.toUpperCase()) : ''}</div>
      <div class="big">${esc(g.title)}</div><div class="s">${esc(it.sub)} · ${LS.formatTime(it.duration)}</div>
      <button class="primary" data-act="play" data-key="${esc(it.key)}" data-list="releases">${LS.icon('play')} Reproducir</button></div>
      <div class="dots">${S.releases.map((_, i) => `<span class="${i === S.heroIndex % S.releases.length ? 'on' : ''}"></span>`).join('')}</div>`;
  }
  setInterval(() => { if (!document.hidden && S.releases.length > 1 && S.page === 'home' && !S.query) { S.heroIndex = (S.heroIndex + 1) % S.releases.length; renderHero(); } }, 9000);

  // ---------- Música ----------
  async function renderMusic(v, id) {
    const q = S.query;
    v.innerHTML = `<h1>Música</h1><p class="hint">${q ? `Canciones para «${esc(q)}». Solo música, sin vídeo.` : 'Elige un género o busca un artista. Aquí solo hay música y suena sin vídeo.'}</p>
      ${q ? '' : chips(LS.GENRES.map((g) => [g, g]), S.genre, 'genre')}
      <section><h2>${q ? 'Música en YouTube' : esc(S.genre)}</h2><div id="hits">${skeleton()}</div></section>`;
    await showResults('#hits', id, 'music');
  }

  // ---------- Vídeos ----------
  async function renderVideos(v, id) {
    const q = S.query;
    v.innerHTML = `<h1>Vídeos</h1><p class="hint">${q ? `Vídeos para «${esc(q)}». Solo vídeos.` : 'Eventos, series, YouTubers y más. Busca para encontrar cualquier vídeo.'}</p>
      <div id="hits">${q ? skeleton() : ''}</div>`;
    if (q) { await showResults('#hits', id, 'videos'); return; }
    if (S.shelves) { drawShelves(id); return; }
    $('#hits').innerHTML = skeleton(6);
    if (S.shelvesLoading) return;
    S.shelvesLoading = true;
    try {
      const built = await Promise.all(LS.SHELVES.map(async (sh) => {
        const results = await Promise.all(sh.queries.map((qq) => ytSearch(qq, { videosOnly: true }).catch((e) => { S.lastError = e.message; return []; })));
        return { title: sh.title, items: LS.mergeShelf(results, 10).map(ytItem) };
      }));
      S.shelves = built.filter((s) => s.items.length);
      if (!S.shelves.length) S.shelves = null;   // se reintentará la próxima vez
      built.forEach((s, i) => setList('shelf' + i, s.items));
    } finally { S.shelvesLoading = false; }
    if (S.page === 'videos' && !S.query) drawShelves(S.renderId);
  }

  function drawShelves(id) {
    if (id !== S.renderId) return;
    const box = $('#hits');
    if (!S.shelves || !S.shelves.length) { box.innerHTML = `<p class="hint err">No se pudieron cargar los vídeos. ${esc(S.lastError || 'Comprueba la conexión.')}</p>`; return; }
    S.shelves.forEach((s, i) => lists.set('shelf' + i, s.items.map((x) => x.key)));
    box.innerHTML = S.shelves.map((s, i) => `<section><h2>${esc(s.title)}</h2><div class="shelf">${s.items.map((it, n) => card(it, 'shelf' + i, 'video', n)).join('')}</div></section>`).join('');
  }

  // ---------- Biblioteca (guardado, listas y descargas) ----------
  function renderLibrary(v) {
    const pls = store.playlists.all();
    const tabs = [['saved', 'Guardado'], ['downloads', 'Descargas'], ...pls.map((p) => ['pl:' + p.id, p.name])];
    v.innerHTML = `<h1>Mi biblioteca</h1>
      <div class="chips">${tabs.map(([k, l]) => `<button class="chip ${k === S.sub ? 'on' : ''}" data-sub="${esc(k)}">${esc(l)}</button>`).join('')}
      <button class="chip add" data-act="newlist">+ Nueva lista</button>
      <button class="chip add" data-act="import">${LS.icon('import')} Importar</button></div><div id="libBody"></div>`;
    if (S.sub === 'downloads') return renderDownloads($('#libBody'));
    const q = LS.fold(S.query);
    const pl = S.sub.startsWith('pl:') ? store.playlists.find(S.sub.slice(3)) : null;
    const src = pl ? pl.items : store.library.items();
    const items = setList('lib', src.filter((i) => !q || LS.fold(i.title + ' ' + i.sub).includes(q)).map(savedItem));
    $('#libBody').innerHTML = `${pl ? `<div class="row-actions"><button class="primary" data-act="playall">${LS.icon('play')} Reproducir todo</button><button data-act="dellist" data-id="${esc(pl.id)}">Eliminar lista</button></div>` : ''}
      ${items.length ? grid('lib', items, 'video') : `<p class="hint">${q ? 'Nada guardado coincide con la búsqueda.' : pl ? 'Lista vacía. Reproduce algo y pulsa + en la barra inferior para añadirlo.' : 'Aún no has guardado nada. Reproduce algo y pulsa el corazón de la barra inferior.'}</p>`}`;
  }

  // ---------- Descargas del contenido libre ----------
  async function loadCatalog() {
    if (S.catalog.length) return S.catalog;
    try { S.catalog = JSON.parse(await Native.fetchText(CATALOG_URL)).items.map(catItem); } catch (e) { S.catalog = []; }
    return S.catalog;
  }

  async function renderDownloads(box) {
    if (!Native.canDownload) {
      box.innerHTML = '<p class="hint">Las descargas están disponibles en las aplicaciones de iOS, Android y Windows. YouTube no permite descargar sus vídeos: aquí solo hay contenido libre (Creative Commons).</p>';
      return;
    }
    box.innerHTML = '<div class="busy">Cargando…</div>';
    const cat = await loadCatalog();
    if (S.page !== 'library' || S.sub !== 'downloads') return;
    const done = store.downloads.all();
    const rows = (items, btn) => items.map((d) => `<div class="drow" data-id="${esc(d.id)}"><img alt="" src="${esc(d.thumb || '')}"><div class="info"><b>${esc(d.title)}</b><span>${esc(d.sub || '')}${d.bytes ? ' · ' + Math.round(d.bytes / 1e6) + ' MB' : ''}</span><progress max="100" value="0" hidden></progress></div><div class="acts">${btn}</div></div>`).join('');
    const todo = cat.filter((c) => !store.downloads.find(c.id));
    box.innerHTML = `<h2>Descargado</h2>${done.length ? rows(done, '<button data-act="playdl">Reproducir</button><button data-act="deldl">Eliminar</button>') : '<p class="hint">Todavía no has descargado nada.</p>'}
      <h2>Contenido libre para descargar</h2><p class="hint">Películas y cortos con licencia Creative Commons. YouTube no permite descargar sus vídeos, por eso aquí solo aparece contenido libre.</p>
      ${todo.length ? rows(todo, '<button data-act="download">Descargar</button>') : '<p class="hint">Ya has descargado todo el contenido libre disponible.</p>'}`;
    done.forEach((d) => lists.set('dl:' + d.id, [savedItem(Object.assign({ src: 'cat' }, d)).key]));
  }

  async function startDownload(row) {
    const it = reg.get('cat:' + row.dataset.id);
    if (!it) return;
    const btn = $('button', row), bar = $('progress', row);
    btn.disabled = true; bar.hidden = false;
    try {
      const res = await Native.call('download', { id: it.id, url: it.url, filename: it.id + (it.url.match(/\.(mp4|webm|mp3|m4a)(\?|$)/i) || [, 'mp4'])[1].toLowerCase() });
      store.downloads.add({ id: it.id, src: 'cat', title: it.title, sub: it.sub, thumb: it.thumb, duration: it.duration, kind: it.kind, url: res.url, bytes: res.bytes, file: res.file });
      toast('Descarga completada: ' + it.title);
      render();
    } catch (e) {
      btn.disabled = false; bar.hidden = true; btn.textContent = 'Reintentar';
      toast('No se pudo descargar «' + it.title + '»: ' + e.message);
    }
  }
  Native.onEvent((e) => {
    if (e && e.type === 'progress') { const bar = $(`.drow[data-id="${e.id}"] progress`); if (bar) bar.value = Math.round(e.p * 100); }
  });

  // ---------- Ajustes ----------
  function renderSettings(v) {
    const s = store.settings.get();
    v.innerHTML = `<h1>Ajustes</h1>
      <section class="panel"><h2>Aspecto</h2>
        <div class="chips">${LS.LOOKS.map(([id, name, , accent]) => `<button class="chip ${id === s.look ? 'on' : ''}" data-look="${id}"><i class="dot" style="background:${accent}"></i>${esc(name)}</button>`).join('')}</div>
        <p class="note">Elige el estilo: cambia colores, brillo y animaciones al momento. «Clásico» es el aspecto de siempre; los demás se apagan solos si tu móvil tiene «reducir movimiento».</p></section>
      <section class="panel"><h2>Ecualizador</h2>
        <label class="switch"><input type="checkbox" id="eqOn" ${s.eqEnabled ? 'checked' : ''}> Activar ecualizador</label>
        <label class="field">Ajuste <select id="eqPreset">${[...Object.keys(LS.EQ_PRESETS), LS.EQ_CUSTOM].map((n) => `<option ${n === s.eqPreset ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select></label>
        <div class="eq" id="eqBands">${s.eqBands.map((b, i) => `<label><span>${LS.EQ_LABELS[i]}</span><input type="range" min="-12" max="12" step="1" value="${b}" data-band="${i}"><output>${fmtGain(b)}</output></label>`).join('')}</div>
        <p id="eqStatus" class="hint"></p>
        <p class="note">Actúa sobre el sonido de YouTube dentro de la app. Los vídeos del catálogo libre no admiten ecualizador.</p></section>
      <section class="panel"><h2>Privacidad</h2>
        <label class="switch"><input type="checkbox" id="adOn" ${s.adBlock ? 'checked' : ''}> Bloquear anuncios y rastreadores</label>
        <p class="note">Corta las conexiones a servidores de publicidad conocidos. Los anuncios que YouTube sirve desde sus propios servidores de vídeo pueden seguir apareciendo.</p></section>
      <section class="panel"><h2>Acerca de</h2><p class="note">Nsound es gratuita, de código abierto y sin suscripciones. YouTube se reproduce con su reproductor oficial y no se descarga. Letras: LRCLIB. Plataforma: ${esc(Native.platform)}.</p></section>`;
    refreshEqStatus();
  }
  const fmtGain = (n) => (n > 0 ? '+' + n : String(n));

  async function refreshEqStatus() {
    const el = $('#eqStatus');
    if (!el) return;
    if (!store.settings.get().eqEnabled) { el.textContent = 'Ecualizador desactivado.'; return; }
    const st = await Player.eqStatus();
    if (!$('#eqStatus')) return;
    el.textContent = !st ? 'Se aplicará cuando reproduzcas algo de YouTube.'
      : st.attached ? (st.level > 0 ? `Activo: el sonido de YouTube pasa por el ecualizador (nivel ${Math.round(st.level)}).` : 'Conectado al reproductor; esperando sonido.')
      : st.error ? 'No se pudo conectar: ' + st.error : 'Esperando a que empiece el sonido de YouTube…';
  }
  setInterval(() => { if (!document.hidden && S.page === 'settings') refreshEqStatus(); }, 2500);

  function applyEq() { const s = store.settings.get(); Player.setEq(s.eqBands, s.eqEnabled); }

  // ---------- Reproducción ----------
  function resolveLocal(it) {
    if (it.src !== 'cat') return it;
    const d = store.downloads.find(it.id);
    return d ? Object.assign({}, it, { url: d.url }) : it;
  }

  function play(key, listId) {
    const it = reg.get(key);
    if (!it) return;
    const keys = lists.get(listId) || [key];
    S.queue = keys.map((k) => reg.get(k)).filter(Boolean);
    S.index = Math.max(0, S.queue.findIndex((i) => i.key === key));
    S.audioOnly = scopeNow() === 'track' && it.src === 'yt';
    S.skips = 0;
    start();
  }

  function start() {
    const it = S.queue[S.index];
    if (!it) return;
    S.now = it; S.time = 0; S.duration = it.duration || 0; S.lyrics = null; S.lyricsFor = ''; S.lyricIndex = -1;
    Player.load(resolveLocal(it), { audioOnly: S.audioOnly });
    showVideo(!S.audioOnly);
    updateBar();
    if (!$('#lyrics').hidden) loadLyrics();
  }

  function next() { if (S.index + 1 < S.queue.length) { S.index++; start(); } else Player.pause(); }
  function prev() { if (S.time > 3 || S.index <= 0) Player.seek(0); else { S.index--; start(); } }

  // El vídeo no se oculta con display:none (el navegador suspendería la reproducción): se deja vivo y casi invisible.
  function showVideo(show) { $('#videoLayer').classList.toggle('mini', !show); }


  // ---------- Pantalla de bloqueo y controles del sistema ----------
  const msOk = typeof navigator !== 'undefined' && 'mediaSession' in navigator && typeof MediaMetadata !== 'undefined';
  let msPos = 0;
  function mediaSessionSet(it) {
    const m = LS.mediaMeta(it);
    if (msOk) {
      try { navigator.mediaSession.metadata = new MediaMetadata(m); } catch (e) { /* formato no admitido */ }
    }
    if (Native.platform === 'ios') Native.call('nowPlaying', { title: m.title, artist: m.artist, album: m.album, artwork: m.artwork[0] ? m.artwork[0].src : '', duration: it.duration || 0, position: 0, playing: true }).catch(() => {});
  }
  function mediaSessionTick(st) {
    if (msOk) {
      try {
        navigator.mediaSession.playbackState = st.playing ? 'playing' : 'paused';
        const now = Date.now();
        if (now - msPos > 1000) {
          msPos = now;
          const pos = LS.mediaPosition(st.time, st.duration > 0 ? st.duration : S.duration);
          if (pos) navigator.mediaSession.setPositionState(pos);
        }
      } catch (e) { /* sin soporte */ }
    }
    if (Native.platform === 'ios' && Date.now() - msPos > 900) {
      msPos = Date.now();
      Native.call('nowPlayingState', { position: st.time || 0, duration: st.duration > 0 ? st.duration : S.duration, playing: !!st.playing }).catch(() => {});
    }
  }
  if (msOk) {
    const set = (a, fn) => { try { navigator.mediaSession.setActionHandler(a, fn); } catch (e) { /* acción no admitida */ } };
    set('play', () => Player.play());
    set('pause', () => Player.pause());
    set('previoustrack', () => prev());
    set('nexttrack', () => next());
    set('seekto', (d) => { if (d && typeof d.seekTime === 'number') Player.seek(d.seekTime); });
    set('seekbackward', null);          // sin «−10 s»: en la pantalla de bloqueo salen anterior y siguiente
    set('seekforward', null);
  }
  // Los botones de la pantalla de bloqueo del iPhone llegan como eventos nativos
  Native.onEvent((e) => {
    if (!e || e.type !== 'remote') return;
    if (e.cmd === 'play') Player.play(); else if (e.cmd === 'pause') Player.pause();
    else if (e.cmd === 'next') next(); else if (e.cmd === 'prev') prev();
    else if (e.cmd === 'seek' && typeof e.position === 'number') Player.seek(e.position);
  });

  Player.on('state', (st) => {
    S.playing = st.playing; S.time = st.time; if (st.duration > 0) S.duration = st.duration;
    mediaSessionTick(st);
    if (st.playing && st.time > 1) S.skips = 0;       // ya suena: se acaba la racha de errores
    LS.setIcon($('#play'), st.playing ? 'pause' : 'play');
    $('#elapsed').textContent = LS.formatTime(S.time);
    $('#total').textContent = LS.formatTime(S.duration);
    const p = $('#progress');
    if (!p.dataset.drag) { p.max = Math.max(1, S.duration); p.value = Math.min(S.time, p.max); p.style.setProperty('--p', (100 * p.value / p.max) + '%'); }
    document.body.classList.toggle('playing', st.playing);
    syncNp();
    followLyrics();
  });
  Player.on('ended', next);
  Player.on('error', (e) => {
    const t = S.now ? S.now.title : '';
    toast(e.code === 101 || e.code === 150 ? `«${t}» no permite reproducirse fuera de youtube.com (lo decide quien lo subió).` : `No se pudo reproducir «${t}» (${e.message || 'error ' + e.code}).`);
    showVideo(false);
    S.skips++;
    if (S.skips <= 3 && S.index + 1 < S.queue.length) setTimeout(next, 1200);
    else if (S.skips > 3) toast('YouTube no deja reproducir estos vídeos ahora mismo. Prueba con otro o inténtalo más tarde.', 7000);
  });

  function updateBar() {
    const it = S.now;
    if (!it) return;
    document.body.classList.add('has-track');
    artHue(it.thumb, it.id, (h) => document.body.style.setProperty('--h', h));
    recentPush(it);
    mediaSessionSet(it);
    $$('.card.cur').forEach((c) => c.classList.remove('cur'));
    $$('.card[data-key]').forEach((c) => { if (c.dataset.key === it.key) c.classList.add('cur'); });
    updateNp();
    $('#nowTitle').textContent = it.title;
    $('#nowSub').textContent = it.sub + (it.src === 'yt' ? ' · YouTube' : '');
    $('#nowImg').src = it.thumb || '';
    $('#nowImg').style.visibility = it.thumb ? 'visible' : 'hidden';
    LS.setIcon($('#heart'), store.library.has(it) ? 'heartOn' : 'heart');
    $('#heart').classList.toggle('on', store.library.has(it));
  }

  // ---------- Pantalla «Reproduciendo» (estilo Nsound) ----------
  const buzz = () => { try { navigator.vibrate && navigator.vibrate(8); } catch (e) { /* sin vibración */ } };

  function toggleHeart() {
    if (!S.now) return;
    store.library.toggle(S.now);
    buzz();
    updateBar();
    for (const el of [$('#heart'), $('#npHeart')]) { el.classList.remove('pop'); void el.offsetWidth; el.classList.add('pop'); }
    if (S.page === 'library') render();
  }

  function updateNp() {
    const it = S.now;
    if (!it) return;
    const img = $('#npImg');
    const big = it.src === 'yt' ? `https://i.ytimg.com/vi/${it.id}/hq720.jpg` : (it.thumb || '');   // hq720 no trae bandas negras
    img.onerror = () => { img.onerror = null; img.src = (it.thumb || '').replace('mqdefault', 'hqdefault'); };
    if (img.getAttribute('src') !== big) img.src = big;
    $('#npTitle').textContent = it.title;
    $('#npSub').textContent = it.sub + (it.src === 'yt' ? ' · YouTube' : '');
    const saved = store.library.has(it);
    LS.setIcon($('#npHeart'), saved ? 'heartOn' : 'heart');
    $('#npHeart').classList.toggle('on', saved);
  }

  function syncNp() {
    if (!$('#np').classList.contains('open')) return;
    LS.setIcon($('#npPlay'), S.playing ? 'pause' : 'play');
    $('#npElapsed').textContent = LS.formatTime(S.time);
    $('#npTotal').textContent = LS.formatTime(S.duration);
    const p = $('#npProgress');
    if (!p.dataset.drag) { p.max = Math.max(1, S.duration); p.value = Math.min(S.time, p.max); p.style.setProperty('--p', (100 * p.value / p.max) + '%'); }
  }

  const openNp = () => { if (!S.now) return; updateNp(); $('#np').classList.add('open'); syncNp(); };
  const closeNp = () => $('#np').classList.remove('open');

  $('.now').addEventListener('click', () => { if (document.body.classList.contains('fx')) openNp(); });
  $('#npClose').onclick = closeNp;
  $('#npPrev').onclick = prev;
  $('#npNext').onclick = next;
  $('#npPlay').onclick = () => { buzz(); Player.toggle(); };
  $('#npHeart').onclick = toggleHeart;
  $('#npAdd').onclick = (e) => { e.stopPropagation(); openAddMenu(); };
  $('#npQueue').onclick = () => openQueue();
  $('#npLyrics').onclick = () => { closeNp(); $('#lyricsBtn').click(); };
  $('#npVideo').onclick = () => { closeNp(); if (S.now) showVideo(true); };
  const npp = $('#npProgress');
  npp.addEventListener('input', () => { npp.dataset.drag = '1'; $('#npElapsed').textContent = LS.formatTime(+npp.value); npp.style.setProperty('--p', (100 * npp.value / npp.max) + '%'); });
  npp.addEventListener('change', () => { delete npp.dataset.drag; Player.seek(+npp.value); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeNp(); });
  let swipeY = null;
  $('#np').addEventListener('touchstart', (e) => { swipeY = e.touches[0].clientY; }, { passive: true });
  $('#np').addEventListener('touchend', (e) => { if (swipeY !== null && e.changedTouches[0].clientY - swipeY > 110 && $('#np').scrollTop === 0) closeNp(); swipeY = null; }, { passive: true });

  // ---------- Letra ----------
  async function loadLyrics() {
    const it = S.now;
    if (!it || S.lyricsFor === it.key) return;
    S.lyricsFor = it.key; S.lyrics = null; S.lyricIndex = -1;
    const info = $('#lyricsInfo'), list = $('#lyricsList');
    list.innerHTML = ''; info.textContent = 'Buscando la letra…';
    const g = it.src !== 'yt' ? { artist: it.sub, title: it.title }
      : it.artist ? { artist: it.artist, title: LS.cleanTitle(it.title).title } : LS.guessTrack(it.title, it.sub);
    try {
      const base = 'https://lrclib.net/api/search?';
      let found = LS.pickLyrics(await Native.fetchText(base + 'track_name=' + encodeURIComponent(g.title) + '&artist_name=' + encodeURIComponent(g.artist)), it.duration);
      if (!found) found = LS.pickLyrics(await Native.fetchText(base + 'q=' + encodeURIComponent(g.artist + ' ' + g.title)), it.duration);
      if (S.lyricsFor !== it.key) return;
      S.lyrics = found;
      info.textContent = !found ? `No hay letra para «${g.title}».` : found.synced ? `${g.artist} — ${g.title}` : `${g.artist} — ${g.title} (sin sincronizar)`;
      list.innerHTML = found ? found.lines.map((l, i) => `<li data-i="${i}">${esc(l.text) || '&nbsp;'}</li>`).join('') : '';
    } catch (e) { info.textContent = 'No se pudo cargar la letra: ' + e.message; }
  }

  function followLyrics() {
    if (!S.lyrics || !S.lyrics.synced || $('#lyrics').hidden) return;
    const i = LS.lyricIndexAt(S.lyrics.lines, S.time + 0.3);
    if (i === S.lyricIndex) return;
    S.lyricIndex = i;
    $$('#lyricsList li.on').forEach((li) => li.classList.remove('on'));
    const li = $(`#lyricsList li[data-i="${i}"]`);
    if (li) { li.classList.add('on'); li.scrollIntoView({ block: 'center', behavior: 'smooth' }); }
  }


  // ---------- Importar una canción, lista pública o artista ----------
  const importing = { busy: false, cancel: false };
  function openImport() {
    let o = $('#imp');
    if (!o) { o = document.createElement('div'); o.id = 'imp'; o.className = 'imp'; document.body.appendChild(o); }
    o.innerHTML = `<div class="imp-box" role="dialog" aria-modal="true" aria-label="Importar lista de reproducción">
      <h2>Importar lista de reproducción</h2><p class="imp-sub">Importa una canción, una lista pública o un artista.</p>
      <input id="impUrl" type="url" inputmode="url" autocomplete="off" placeholder="${esc(LS.IMPORT_HINT)}…" aria-label="Enlace">
      <p class="imp-note">${esc(LS.IMPORT_NOTICE)}</p><div id="impOut" class="imp-out" aria-live="polite"></div>
      <div class="imp-btns"><button data-act="importclose">Cancelar</button><button id="impGo" class="primary" data-act="importgo">Importar</button></div></div>`;
    o.hidden = false;
    o.onclick = (e) => { if (e.target === o) closeImport(); };
    o.onkeydown = (e) => { if (e.key === 'Escape') closeImport(); if (e.key === 'Enter' && e.target.id === 'impUrl') runImport(); };
    setTimeout(() => $('#impUrl') && $('#impUrl').focus(), 50);
  }
  function closeImport() { importing.cancel = true; const o = $('#imp'); if (o) o.hidden = true; }

  async function runImport() {
    const out = $('#impOut'), btn = $('#impGo');
    if (importing.busy) { out.textContent = 'Espera un momento: se está terminando la importación anterior.'; return; }
    const link = LS.parseImportLink($('#impUrl').value);
    if (link.error) { out.textContent = link.error; return; }
    importing.busy = true; importing.cancel = false; btn.disabled = true;
    try {
      out.textContent = 'Leyendo el enlace…';
      let text;
      try { text = await Native.fetchText(link.fetchUrl); } catch (e) { throw new Error('No se pudo abrir el enlace (¿es público? ¿hay conexión?).'); }
      const info = LS.parseImported(link, text);
      const wanted = info.tracks;
      if (!wanted.length) throw new Error('No se encontraron canciones. La lista debe ser pública, y el servicio puede haber cambiado su página.');
      const found = [], missing = [];
      let n = 0;
      const one = async (t) => {
        try {
          const res = (await musicSearch((t.artist + ' ' + t.title).trim())).filter((m) => LS.isSong({ title: m.title, duration: m.duration }));
          const m = LS.pickImportMatch(t, res);
          if (m) return musicItem(m);
        } catch (e) { /* esa canción queda como no encontrada */ }
        return null;
      };
      const results = new Array(wanted.length);
      let next = 0;
      await Promise.all([0, 1, 2].map(async () => {
        while (next < wanted.length && !importing.cancel) {
          const i = next++;
          results[i] = await one(wanted[i]);
          out.textContent = `Buscando en YouTube Music… ${++n} de ${wanted.length}`;
        }
      }));
      if (importing.cancel) return;
      wanted.forEach((t, i) => (results[i] ? found.push(results[i]) : missing.push((t.artist ? t.artist + ' — ' : '') + t.title)));
      if (!found.length) throw new Error('No se encontró ninguna de las canciones en YouTube Music.');
      const pl = store.playlists.create(info.name || 'Lista importada');
      found.forEach((it) => store.playlists.toggle(pl.id, it));
      out.innerHTML = `<b>${found.length} de ${wanted.length} canciones</b> añadidas a «${esc(pl.name)}».`
        + (missing.length ? `<details><summary>${missing.length} sin encontrar</summary><ul>${missing.map((m) => `<li>${esc(m)}</li>`).join('')}</ul></details>` : '')
        + `<button class="primary" data-act="importopen" data-id="${esc(pl.id)}">Abrir lista</button>`;
      btn.hidden = true;
    } catch (e) {
      out.textContent = e.message || 'No se pudo importar.';
    } finally { importing.busy = false; btn.disabled = false; }
  }


  // ---------- Cola de reproducción ----------
  function applyQueue(r) { S.queue = r.queue; S.index = r.index; }
  function queueIt(key, mode) {
    const it = reg.get(key);
    if (!it) return;
    if (!S.now || !S.queue.length) { play(key, ''); toast('Reproduciendo «' + it.title + '»'); return; }
    applyQueue(LS.queueAdd(S.queue, S.index, it, mode));
    buzz();
    toast(mode === 'next' ? 'Sonará a continuación: ' + it.title : 'Añadida a la cola: ' + it.title);
    const qp = $('#qp');
    if (qp && !qp.hidden) renderQueue();
  }
  function sheet(id) {
    let o = $('#' + id);
    if (!o) { o = document.createElement('div'); o.id = id; o.className = 'imp'; o.hidden = true; document.body.appendChild(o); }
    return o;
  }
  function openCardMenu(key) {
    const it = reg.get(key);
    if (!it) return;
    const o = sheet('qm');
    o.innerHTML = `<div class="imp-box" role="dialog" aria-modal="true"><h2>${esc(it.title)}</h2><p class="imp-sub">${esc(it.sub)}</p>
      <div class="imp-btns" style="flex-direction:column"><button data-act="qnext" data-key="${esc(key)}">Reproducir a continuación</button>
      <button data-act="qend" data-key="${esc(key)}">Añadir al final de la cola</button><button data-act="qmclose">Cancelar</button></div></div>`;
    o.hidden = false;
    o.onclick = (e) => { if (e.target === o) o.hidden = true; };
  }
  function openQueue() {
    const o = sheet('qp');
    o.hidden = false;
    o.onclick = (e) => { if (e.target === o) o.hidden = true; };
    o.onkeydown = (e) => { if (e.key === 'Escape') o.hidden = true; };
    renderQueue();
  }
  function renderQueue() {
    const o = $('#qp');
    const cur = S.queue[S.index];
    const up = S.queue.map((it, i) => [it, i]).filter(([, i]) => i > S.index);
    const row = (it, i) => `<li><button class="q-main" data-act="qjump" data-i="${i}"><b>${esc(it.title)}</b><span>${esc(it.sub)}</span></button>
      <button class="q-b" aria-label="Subir" data-act="qup" data-i="${i}" ${i <= S.index + 1 ? 'disabled' : ''}>${LS.icon('up')}</button>
      <button class="q-b" aria-label="Bajar" data-act="qdown" data-i="${i}" ${i >= S.queue.length - 1 ? 'disabled' : ''}>${LS.icon('down')}</button>
      <button class="q-b" aria-label="Quitar" data-act="qdel" data-i="${i}">${LS.icon('close')}</button></li>`;
    o.innerHTML = `<div class="imp-box" role="dialog" aria-modal="true" aria-label="Cola de reproducción"><h2>Cola</h2>
      <p class="imp-sub">${cur ? 'Suena ahora: ' + esc(cur.title) : 'No hay nada sonando.'}</p>
      ${up.length ? `<ul class="q-list">${up.map(([it, i]) => row(it, i)).join('')}</ul>` : '<p class="imp-note">No hay más canciones en la cola. Mantén pulsada una tarjeta (o clic derecho) para añadirla.</p>'}
      <div class="imp-btns">${up.length ? '<button data-act="qclear">Vaciar</button>' : ''}<button class="primary" data-act="qpclose">Cerrar</button></div></div>`;
  }
  // Mantener pulsada una tarjeta (o clic derecho) abre su menú de cola
  let pressTimer = 0, suppressClick = 0;
  document.addEventListener('contextmenu', (e) => { const c = e.target.closest && e.target.closest('.card[data-key]'); if (c) { e.preventDefault(); openCardMenu(c.dataset.key); } });
  document.addEventListener('touchstart', (e) => {
    const c = e.target.closest && e.target.closest('.card[data-key]');
    clearTimeout(pressTimer);
    if (c) pressTimer = setTimeout(() => { suppressClick = Date.now() + 600; openCardMenu(c.dataset.key); }, 550);
  }, { passive: true });
  ['touchend', 'touchmove', 'touchcancel'].forEach((ev) => document.addEventListener(ev, () => clearTimeout(pressTimer), { passive: true }));
  document.addEventListener('click', (e) => { if (Date.now() < suppressClick) { e.stopPropagation(); e.preventDefault(); } }, true);

  // ---------- Listas ----------
  function newList(addCurrent) {
    const name = prompt('Nombre de la lista');
    if (!name || !name.trim()) return;
    const p = store.playlists.create(name);
    if (addCurrent && S.now) store.playlists.toggle(p.id, S.now);
    go('#/library/pl/' + p.id);
  }

  function openAddMenu() {
    const m = $('#addMenu');
    if (!S.now) { toast('Reproduce algo para añadirlo a una lista.'); return; }
    m.innerHTML = store.playlists.all().map((p) => `<button data-act="toggleplist" data-id="${esc(p.id)}">${store.playlists.has(p.id, S.now) ? '✓ ' : ''}${esc(p.name)}</button>`).join('')
      + '<button data-act="newlistwith">+ Nueva lista con esta canción…</button>';
    m.hidden = !m.hidden;
  }

  // ---------- Eventos ----------
  document.addEventListener('click', (e) => {
    const t = e.target.closest('[data-act],[data-scope],[data-genre],[data-sub],[data-page],[data-look]');
    if (!t) { $('#addMenu').hidden = true; return; }
    const d = t.dataset;
    if (d.look) { store.settings.set({ look: d.look }); applyLook(d.look); if (S.page === 'settings') render(); return; }
    if (d.page) { go('#/' + d.page); return; }
    if (d.scope) {
      if (S.page === 'home' && d.scope === 'track') { go('#/music'); return; }     // Música y Vídeos abren su apartado directamente
      if (S.page === 'home' && d.scope === 'video') { go('#/videos'); return; }
      S.scope = d.scope; render(); return;
    }
    if (d.genre) { S.genre = d.genre; render(); return; }
    if (d.sub) { go(d.sub === 'saved' ? '#/library' : d.sub === 'downloads' ? '#/library/downloads' : '#/library/pl/' + d.sub.slice(3)); return; }
    switch (d.act) {
      case 'play': play(d.key, d.list); break;
      case 'playall': { const k = lists.get('lib') || []; if (k.length) play(k[0], 'lib'); break; }
      case 'newlist': newList(false); break;
      case 'qnext': queueIt(d.key, 'next'); $('#qm').hidden = true; break;
      case 'qend': queueIt(d.key, 'end'); $('#qm').hidden = true; break;
      case 'qmclose': $('#qm').hidden = true; break;
      case 'qpclose': $('#qp').hidden = true; break;
      case 'qjump': S.index = Number(d.i); S.skips = 0; start(); renderQueue(); break;
      case 'qup': case 'qdown': applyQueue(LS.queueMove(S.queue, S.index, Number(d.i), d.act === 'qup' ? -1 : 1)); renderQueue(); break;
      case 'qdel': applyQueue(LS.queueRemove(S.queue, S.index, Number(d.i))); renderQueue(); break;
      case 'qclear': applyQueue(LS.queueClearUpcoming(S.queue, S.index)); renderQueue(); break;
      case 'import': openImport(); break;
      case 'importgo': runImport(); break;
      case 'importclose': closeImport(); break;
      case 'importopen': { const id = d.id; closeImport(); go('#/library/pl/' + id); break; }
      case 'newlistwith': $('#addMenu').hidden = true; newList(true); break;
      case 'dellist': if (confirm('¿Eliminar esta lista? Las canciones guardadas en Mi biblioteca no se borran.')) { store.playlists.remove(d.id); go('#/library'); } break;
      case 'toggleplist': store.playlists.toggle(d.id, S.now); openAddMenu(); openAddMenu(); break;
      case 'download': startDownload(t.closest('.drow')); break;
      case 'playdl': { const row = t.closest('.drow'); play('cat:' + row.dataset.id, 'dl:' + row.dataset.id); break; }
      case 'deldl': { const row = t.closest('.drow'); const dd = store.downloads.find(row.dataset.id); Native.call('deleteDownload', { file: dd && dd.file }).catch(() => {}); store.downloads.remove(row.dataset.id); render(); break; }
    }
  });

  $('#prev').onclick = prev;
  $('#next').onclick = next;
  $('#play').onclick = () => { if (S.now) Player.toggle(); else { const k = lists.get('releases') || lists.get('home') || lists.get('music'); if (k && k.length) play(k[0], lists.has('releases') ? 'releases' : lists.has('home') ? 'home' : 'music'); } };
  $('#heart').onclick = toggleHeart;
  $('#addList').onclick = (e) => { e.stopPropagation(); openAddMenu(); };
  $('#lyricsBtn').onclick = () => { const l = $('#lyrics'); l.hidden = !l.hidden; if (!l.hidden) { if (S.now) loadLyrics(); else $('#lyricsInfo').textContent = 'Reproduce una canción para ver su letra.'; } };
  $('#lyricsClose').onclick = () => { $('#lyrics').hidden = true; };
  $('#lyricsList').onclick = (e) => { const li = e.target.closest('li'); if (!li || !S.lyrics || !S.lyrics.synced) return; Player.seek(S.lyrics.lines[+li.dataset.i].time); };
  $('#videoBtn').onclick = () => { if (S.now) showVideo($('#videoLayer').classList.contains('mini')); };
  $('#closeVideo').onclick = () => showVideo(false);
  $('#dlBtn').onclick = () => { go('#/library/downloads'); if (S.now && S.now.src === 'yt') toast('YouTube no permite descargar sus vídeos. En Descargas solo hay contenido libre (Creative Commons).'); };

  const prog = $('#progress');
  prog.addEventListener('input', () => { prog.dataset.drag = '1'; $('#elapsed').textContent = LS.formatTime(+prog.value); prog.style.setProperty('--p', (100 * prog.value / prog.max) + '%'); });
  prog.addEventListener('change', () => { delete prog.dataset.drag; Player.seek(+prog.value); });
  $('#volume').addEventListener('input', (e) => { Player.setVolume(+e.target.value); store.settings.set({ volume: +e.target.value }); });

  // Ajustes: ecualizador y anuncios
  document.addEventListener('input', (e) => {
    const t = e.target;
    if (t.dataset && t.dataset.band !== undefined) {
      const bands = store.settings.get().eqBands.slice();
      bands[+t.dataset.band] = +t.value;
      const preset = LS.eqMatch(bands);
      store.settings.set({ eqBands: bands, eqPreset: preset });
      t.nextElementSibling.textContent = fmtGain(+t.value);
      $('#eqPreset').value = preset;
      applyEq();
    }
  });
  document.addEventListener('change', (e) => {
    const t = e.target;
    if (t.id === 'eqOn') { store.settings.set({ eqEnabled: t.checked }); applyEq(); refreshEqStatus(); }
    if (t.id === 'eqPreset' && LS.EQ_PRESETS[t.value]) { store.settings.set({ eqPreset: t.value, eqBands: LS.EQ_PRESETS[t.value] }); applyEq(); render(); }
    if (t.id === 'adOn') { store.settings.set({ adBlock: t.checked }); Native.call('setAdBlock', { enabled: t.checked }).catch(() => {}); }
  });

  let typing;
  $('#q').addEventListener('input', (e) => {
    clearTimeout(typing);
    typing = setTimeout(() => {
      S.query = e.target.value.trim();
      if (S.page === 'settings') go('#/home'); else render();
    }, 600);
  });

  $('#q').addEventListener('focus', () => document.body.classList.add('kb'));
  $('#q').addEventListener('blur', () => document.body.classList.remove('kb'));
  window.addEventListener('hashchange', route);

  // ---------- Arranque ----------
  Player.init();
  const s0 = store.settings.get();

  // Deslizar hacia abajo cierra «Reproduciendo» (el panel sigue al dedo)
  (function () {
    const np = $('#np'); let y0 = 0, dy = 0, on = false;
    np.addEventListener('touchstart', (e) => { if (np.scrollTop > 0 || e.target.closest('input,button')) return; y0 = e.touches[0].clientY; dy = 0; on = true; }, { passive: true });
    np.addEventListener('touchmove', (e) => {
      if (!on) return;
      dy = e.touches[0].clientY - y0;
      if (dy > 0) { np.style.transition = 'none'; np.style.transform = `translateY(${dy}px)`; }
    }, { passive: true });
    const end = () => {
      if (!on) return; on = false;
      np.style.transition = ''; np.style.transform = '';
      if (dy > 110) { buzz(); closeNp(); }
    };
    np.addEventListener('touchend', end); np.addEventListener('touchcancel', end);
  })();

  // Pantalla de arranque
  window.addEventListener('load', () => { const sp = $('#splash'); if (!sp) return; setTimeout(() => { sp.classList.add('out'); setTimeout(() => sp.remove(), 500); }, 450); });

  // Funciona sin conexión (solo en un navegador; las apps ya llevan la interfaz dentro)
  if (Native.platform === 'web' && 'serviceWorker' in navigator && /^https:|^http:\/\/localhost/.test(location.href)) {
    navigator.serviceWorker.register('sw.js').catch(() => { /* sin soporte: no pasa nada */ });
  }
  applyLook(s0.look);
  $('#volume').value = s0.volume;
  Player.setVolume(s0.volume);
  applyEq();
  Native.call('setAdBlock', { enabled: s0.adBlock }).catch(() => {});
  loadCatalog();
  route();
})();
