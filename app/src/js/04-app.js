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

  // ---------- Estado ----------
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

  const hue = (id) => { let h = 17; for (const c of String(id)) h = (h * 31 + c.charCodeAt(0)) | 0; return Math.abs(h) % 360; };

  // ---------- Tarjetas ----------
  function card(it, listId, shape, idx) {
    return `<button class="card ${shape}" style="--i:${Math.min(idx || 0, 12)}" data-act="play" data-key="${esc(it.key)}" data-list="${esc(listId)}">
      <div class="thumb" style="--h:${hue(it.id)}">${it.thumb ? `<img loading="lazy" alt="" src="${esc(it.thumb)}" onerror="this.remove()">` : ''}
      ${it.duration > 0 ? `<span class="dur">${LS.formatTime(it.duration)}</span>` : (it.src === 'yt' ? '<span class="dur">EN DIRECTO</span>' : '')}<span class="play" aria-hidden="true">▶</span></div>
      <div class="t">${esc(it.title)}</div><div class="s">${esc(it.sub)}</div></button>`;
  }

  function setList(id, items) { lists.set(id, items.map((i) => i.key)); return items; }
  const grid = (id, items, shape) => `<div class="grid ${shape}">${items.map((i, n) => card(i, id, shape, n)).join('')}</div>`;
  const skeleton = (n) => `<div class="grid video">${Array.from({ length: n || 8 }, () => '<div class="skel"></div>').join('')}</div>`;

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
    v.innerHTML = `<h1>Inicio</h1>${chips([['all', 'Todo'], ['track', 'Música'], ['video', 'Vídeos']], sc, 'scope')}
      ${q ? `<p class="hint">${sc === 'track' ? 'Canciones' : sc === 'video' ? 'Vídeos' : 'Resultados'} para «${esc(q)}».</p>` : ''}
      <section id="hero" class="hero" hidden></section>
      <section><h2 id="hitsTitle">${q ? 'Resultados en YouTube' : sc === 'track' ? 'Canciones del momento' : sc === 'video' ? 'Videoclips del momento' : 'Éxitos del momento'}</h2>
      <div id="hits">${skeleton()}</div></section>
      <section id="savedRow" hidden><h2>De tu biblioteca</h2><div id="savedGrid"></div></section>`;
    if (!q) { renderHero(); setTimeout(() => loadReleases(id), 1500); }
    const saved = store.library.items().slice(0, 8).map(savedItem);
    if (!q && saved.length) { $('#savedRow').hidden = false; $('#savedGrid').innerHTML = grid(setList('home-saved', saved) && 'home-saved', saved, 'video'); }
    await showResults('#hits', id, 'home');
  }

  async function showResults(target, id, listId) {
    const sc = scopeNow(), q = S.query.trim();
    const box = $(target);
    try {
      let list;
      if (q.length === 1) { box.innerHTML = '<p class="hint">Escribe al menos dos letras.</p>'; return; }
      if (q) list = await ytSearch(sc === 'track' ? q + ' audio' : q, { videosOnly: true });
      else if (S.page === 'music' && S.genre !== LS.GENRES[0]) list = await ytSearch(S.genre + ' official music video', { videosOnly: true });
      else list = await ytSearch('official music video ' + new Date().getFullYear(), { videosOnly: true });
      if (id !== S.renderId) return;
      list = sc === 'track' ? list.filter(LS.isSong) : sc === 'video' ? list.filter(LS.isVideo) : (q ? list.filter((x) => !LS.isLive(x)) : list.filter(LS.isSong));
      const items = setList(listId, list.map(ytItem));
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
      <button class="primary" data-act="play" data-key="${esc(it.key)}" data-list="releases">▶ Reproducir</button></div>
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
      <button class="chip add" data-act="newlist">+ Nueva lista</button></div><div id="libBody"></div>`;
    if (S.sub === 'downloads') return renderDownloads($('#libBody'));
    const q = LS.fold(S.query);
    const pl = S.sub.startsWith('pl:') ? store.playlists.find(S.sub.slice(3)) : null;
    const src = pl ? pl.items : store.library.items();
    const items = setList('lib', src.filter((i) => !q || LS.fold(i.title + ' ' + i.sub).includes(q)).map(savedItem));
    $('#libBody').innerHTML = `${pl ? `<div class="row-actions"><button class="primary" data-act="playall">▶ Reproducir todo</button><button data-act="dellist" data-id="${esc(pl.id)}">Eliminar lista</button></div>` : ''}
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
        <label class="field">Estilo <select id="look"><option value="nsound" ${s.look !== 'clasico' ? 'selected' : ''}>Nsound (prueba)</option><option value="clasico" ${s.look === 'clasico' ? 'selected' : ''}>Clásico</option></select></label>
        <p class="note">El estilo «Nsound» usa los colores del logo, brillo ambiental y animaciones suaves (se apagan solas si tu móvil tiene «reducir movimiento»).</p></section>
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

  Player.on('state', (st) => {
    S.playing = st.playing; S.time = st.time; if (st.duration > 0) S.duration = st.duration;
    if (st.playing && st.time > 1) S.skips = 0;       // ya suena: se acaba la racha de errores
    $('#play').textContent = st.playing ? '⏸' : '▶';
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
    document.body.style.setProperty('--h', hue(it.id));
    updateNp();
    $('#nowTitle').textContent = it.title;
    $('#nowSub').textContent = it.sub + (it.src === 'yt' ? ' · YouTube' : '');
    $('#nowImg').src = it.thumb || '';
    $('#nowImg').style.visibility = it.thumb ? 'visible' : 'hidden';
    $('#heart').textContent = store.library.has(it) ? '♥' : '♡';
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
    $('#npHeart').textContent = saved ? '♥' : '♡';
    $('#npHeart').classList.toggle('on', saved);
  }

  function syncNp() {
    if (!$('#np').classList.contains('open')) return;
    $('#npPlay').textContent = S.playing ? '⏸' : '▶';
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
    const g = it.src === 'yt' ? LS.guessTrack(it.title, it.sub) : { artist: it.sub, title: it.title };
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
    const t = e.target.closest('[data-act],[data-scope],[data-genre],[data-sub],[data-page]');
    if (!t) { $('#addMenu').hidden = true; return; }
    const d = t.dataset;
    if (d.page) { go('#/' + d.page); return; }
    if (d.scope) { S.scope = d.scope; render(); return; }
    if (d.genre) { S.genre = d.genre; render(); return; }
    if (d.sub) { go(d.sub === 'saved' ? '#/library' : d.sub === 'downloads' ? '#/library/downloads' : '#/library/pl/' + d.sub.slice(3)); return; }
    switch (d.act) {
      case 'play': play(d.key, d.list); break;
      case 'playall': { const k = lists.get('lib') || []; if (k.length) play(k[0], 'lib'); break; }
      case 'newlist': newList(false); break;
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
    if (t.id === 'look') { store.settings.set({ look: t.value }); document.body.classList.toggle('fx', t.value !== 'clasico'); }
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
  document.body.classList.toggle('fx', s0.look !== 'clasico');
  $('#volume').value = s0.volume;
  Player.setVolume(s0.volume);
  applyEq();
  Native.call('setAdBlock', { enabled: s0.adBlock }).catch(() => {});
  loadCatalog();
  route();
})();
