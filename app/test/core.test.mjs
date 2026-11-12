import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
require('../src/js/00-core.js');
require('../src/js/00-import.js');
require('../src/js/00-queue.js');
require('../src/js/00-media.js');
require('../src/js/01-stores.js');
const LS = globalThis.LS;

// Mismos 21 casos que tools/test_title_regex.py, MetadataMatcherTests.swift y MetadataMatcherTests.cs
const CASES = [
  ['Song Name (Official Video)', 'Song Name', [], 'original'],
  ['Song Name [Official Music Video] [4K]', 'Song Name', [], 'original'],
  ['Song Name (Lyrics)', 'Song Name', [], 'original'],
  ['Song Name - Official Audio', 'Song Name', [], 'original'],
  ['Song Name (2011 Remaster)', 'Song Name', [], 'original'],
  ['Song Name - Remastered 2009', 'Song Name', [], 'original'],
  ['Song Name - 2015 Remaster', 'Song Name', [], 'original'],
  ['Song Name (feat. Artist B)', 'Song Name', ['Artist B'], 'original'],
  ['Song Name ft. Artist B & Artist C', 'Song Name', ['Artist B', 'Artist C'], 'original'],
  ['Song Name (feat. Artist B) [Official Video]', 'Song Name', ['Artist B'], 'original'],
  ['Song Name (Artist X Remix)', 'Song Name', [], 'remix'],
  ['Song Name - Club Mix', 'Song Name', [], 'remix'],
  ['Song Name (Live at Wembley)', 'Song Name', [], 'live'],
  ['Song Name - En Vivo', 'Song Name', [], 'live'],
  ['Song Name (Acoustic Version)', 'Song Name', [], 'acoustic'],
  ['Song Name (Radio Edit)', 'Song Name', [], 'edit'],
  ['Song Name (feat. B) (Live) [HD]', 'Song Name', ['B'], 'live'],
  ['Livewire', 'Livewire', [], 'original'],
  ['Remix Culture', 'Remix Culture', [], 'original'],
  ['Bailando con Lobos', 'Bailando con Lobos', [], 'original'],
  ['Señorita (Video Oficial)', 'Señorita', [], 'original'],
];

test('limpieza de títulos: 21 casos borde', () => {
  for (const [raw, title, featuring, version] of CASES)
    assert.deepEqual(LS.cleanTitle(raw), { title, featuring, version }, raw);
});

test('adivinar artista y título de un vídeo', () => {
  assert.deepEqual(LS.guessTrack('Queen - Bohemian Rhapsody (Official Video Remastered)', 'Queen Official'), { artist: 'Queen', title: 'Bohemian Rhapsody' });
  assert.deepEqual(LS.guessTrack('Love of My Life', 'Queen Official'), { artist: 'Queen', title: 'Love of My Life' });
  assert.deepEqual(LS.guessTrack('Blinding Lights (Official Audio)', 'TheWeekndVEVO'), { artist: 'TheWeeknd', title: 'Blinding Lights' });
  assert.deepEqual(LS.guessTrack('Song', 'Artist - Topic'), { artist: 'Artist', title: 'Song' });
});

test('duraciones', () => {
  assert.equal(LS.parseLength('3:25'), 205);
  assert.equal(LS.parseLength('1:02:03'), 3723);
  assert.equal(LS.parseLength('EN DIRECTO'), 0);
  assert.equal(LS.parseLength(null), 0);
  assert.equal(LS.formatTime(205), '3:25');
  assert.equal(LS.formatTime(3723), '1:02:03');
  assert.equal(LS.formatTime(NaN), '0:00');
});

const HTML = `<html><script>var ytInitialData = {"contents":{"list":[
  {"videoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"Canción uno"}]},
    "ownerText":{"runs":[{"text":"Canal A"}]},"lengthText":{"simpleText":"3:25"},"publishedTimeText":{"simpleText":"hace 5 d"}}},
  {"shelf":{"items":[{"videoRenderer":{"videoId":"ABC_def-123","title":{"simpleText":"Directo"},
    "longBylineText":{"runs":[{"text":"Canal B"}]}}}]}},
  {"videoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"Duplicado"}]}}},
  {"videoRenderer":{"videoId":"malo","title":{"runs":[{"text":"Id inválido"}]}}}
]}};</script></html>`;

test('análisis de resultados de YouTube', () => {
  const r = LS.parseYouTube(HTML);
  assert.equal(r.length, 2);
  assert.deepEqual(r[0], { id: 'abcdefghijk', title: 'Canción uno', channel: 'Canal A', duration: 205, published: 'hace 5 d' });
  assert.equal(r[1].channel, 'Canal B');
  assert.equal(LS.isLive(r[1]), true);
  assert.deepEqual(LS.parseYouTube('<html></html>'), []);
  assert.deepEqual(LS.parseYouTube('var ytInitialData = {roto};</script>'), []);
  assert.equal(LS.isValidVideoId("');alert(1)//"), false);
});

test('URL de búsqueda con filtros', () => {
  assert.equal(LS.searchUrl(' rock ', {}), 'https://www.youtube.com/results?search_query=rock');
  assert.ok(LS.searchUrl('rock', { videosOnly: true }).endsWith('&sp=EgIQAQ%3D%3D'));
  assert.ok(LS.searchUrl('rock', { thisWeek: true }).endsWith('&sp=EgQIAxAB'));
});

test('filtro música / vídeo', () => {
  const v = (title, duration) => ({ id: 'abcdefghijk', title, duration });
  const cases = [
    ['Queen - Love of My Life', 217, true, true],
    ['Queen - Love of My Life (Official Audio)', 217, true, false],
    ['Queen - Love of My Life (Lyrics)', 217, true, false],
    ['Piano tutorial: Love of My Life', 300, false, true],
    ['Best rock mix 2026', 300, false, true],
    ['Remix Culture - DJ Snake', 200, true, true],
    ['Rock hits - 2 hours', 7200, false, true],
    ['Álbum completo de rock', 300, false, false],
    ['Cómo tocar la canción', 300, false, true],
    ['Canción larga', 900, false, true],
    ['Directo permanente', 0, false, false],
  ];
  for (const [title, d, song, video] of cases) {
    assert.equal(LS.isSong(v(title, d)), song, 'canción: ' + title);
    assert.equal(LS.isVideo(v(title, d)), video, 'vídeo: ' + title);
  }
});

test('mezcla de estanterías', () => {
  const V = (id, title, duration) => ({ id: id.padEnd(11, 'x'), title, duration });
  const a = [V('a1', 'Velada combate', 600), V('a2', 'Directo', 0), V('a3', 'Resumen', 900)];
  const b = [V('b1', 'Serie tráiler', 120), V('a1', 'Velada combate', 600), V('b2', 'Short', 20), V('b3', 'Canción (Official Audio)', 200)];
  assert.deepEqual(LS.mergeShelf([a, b], 10).map((x) => x.id), ['a1xxxxxxxxx', 'b1xxxxxxxxx', 'a3xxxxxxxxx']);
  assert.equal(LS.mergeShelf([a, b], 1).length, 1);
  assert.deepEqual(LS.mergeShelf([], 5), []);
  assert.ok(LS.SHELVES.length >= 5 && LS.SHELVES.every((s) => s.queries.length > 0));
});

test('letras sincronizadas', () => {
  const lines = LS.parseLrc('[00:12.50] Hola\n[ar: Artista]\n[01:02.5][00:05] Coro\nsin marca\n[00:20:30]Fin');
  assert.equal(lines.length, 4);
  assert.deepEqual(lines[0], { time: 5, text: 'Coro' });
  assert.equal(lines[1].time, 12.5);
  assert.ok(Math.abs(lines[2].time - 20.3) < 1e-9);
  assert.equal(lines[3].time, 62.5);
  const l = LS.parseLrc('[00:05] a\n[00:10] b\n[00:15] c');
  assert.deepEqual([2, 5, 14.9, 999].map((p) => LS.lyricIndexAt(l, p)), [-1, 0, 1, 2]);

  const json = [{ duration: 200, plainLyrics: 'solo texto', syncedLyrics: null },
    { duration: 260, plainLyrics: 'x', syncedLyrics: '[00:01.00] lejos' },
    { duration: 201, plainLyrics: 'x', syncedLyrics: '[00:01.00] cerca' }];
  const best = LS.pickLyrics(json, 200);
  assert.equal(best.synced, true);
  assert.equal(best.lines[0].text, 'cerca');
  const plain = LS.pickLyrics([{ duration: 200, plainLyrics: 'uno\ndos' }], 200);
  assert.equal(plain.synced, false);
  assert.equal(plain.lines.length, 2);
  assert.equal(LS.pickLyrics('[]', 200), null);
  assert.equal(LS.pickLyrics('no es json', 200), null);
});

test('ecualizador: ajustes predefinidos', () => {
  assert.equal(LS.EQ_LABELS.length, 10);
  for (const [name, bands] of Object.entries(LS.EQ_PRESETS)) {
    assert.equal(bands.length, 10, name);
    assert.ok(bands.every((b) => b >= -12 && b <= 12), name);
    assert.equal(LS.eqMatch(bands), name);
  }
  assert.equal(LS.eqMatch([1, 0, 0, 0, 0, 0, 0, 0, 0, 0]), LS.EQ_CUSTOM);
});

test('bloqueo de anuncios', () => {
  const cases = [
    ['https://googleads.g.doubleclick.net/pagead/id', true],
    ['https://www.youtube.com/pagead/adview?x=1', true],
    ['https://www.youtube.com/api/stats/ads?ver=2', true],
    ['https://www.youtube.com/iframe_api', false],
    ['https://www.youtube.com/embed/dQw4w9WgXcQ', false],
    ['https://rr1---sn-abc.googlevideo.com/videoplayback?id=1', false],
    ['https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg', false],
    ['https://notdoubleclick.net.example.com/x', false],
    ['no es una url', false],
  ];
  for (const [url, blocked] of cases) assert.equal(LS.isAdUrl(url), blocked, url);
});

// ---------- Almacenamiento ----------
function fakeStorage(initial = {}) {
  const m = new Map(Object.entries(initial));
  return { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), _m: m };
}

test('ajustes: se corrigen valores fuera de rango y archivos rotos', () => {
  const st = fakeStorage();
  const s = LS.createStores(st);
  assert.equal(s.settings.get().volume, 80);
  assert.equal(s.settings.get().adBlock, true);
  s.settings.set({ eqEnabled: true, eqBands: [30, -30, 1.4, NaN], volume: 250, eqPreset: 'No existe' });
  const again = LS.createStores(st).settings.get();
  assert.equal(again.eqEnabled, true);
  assert.deepEqual(again.eqBands, [12, -12, 1, 0, 0, 0, 0, 0, 0, 0]);
  assert.equal(again.volume, 100);
  assert.equal(again.eqPreset, LS.EQ_CUSTOM);
  assert.equal(LS.createStores(fakeStorage({ 'ls.settings': '{ roto' })).settings.get().volume, 80);
});

test('biblioteca, listas y descargas se guardan y se recuperan', () => {
  const st = fakeStorage();
  const item = { src: 'yt', id: 'abcdefghijk', title: 'Canción', sub: 'Canal', duration: 205, thumb: 'x', extra: 'se descarta' };
  const s = LS.createStores(st);
  assert.equal(s.library.toggle(item), true);
  assert.equal(LS.createStores(st).library.has(item), true);
  assert.equal(LS.createStores(st).library.items()[0].extra, undefined);
  assert.equal(s.library.toggle(item), false);

  const p = s.playlists.create('  Favoritas  ');
  assert.equal(p.name, 'Favoritas');
  assert.equal(s.playlists.toggle(p.id, item), true);
  assert.equal(LS.createStores(st).playlists.has(p.id, item), true);
  assert.equal(s.playlists.toggle(p.id, item), false);
  assert.equal(s.playlists.toggle('no-existe', item), false);
  s.playlists.remove(p.id);
  assert.equal(LS.createStores(st).playlists.all().length, 0);

  s.downloads.add({ id: 'v1', title: 'Vídeo', url: 'https://x/y.mp4', bytes: 5 });
  assert.equal(LS.createStores(st).downloads.find('v1').bytes, 5);
  s.downloads.remove('v1');
  assert.equal(LS.createStores(st).downloads.all().length, 0);
});

test('caché con caducidad: guarda, caduca y se limita', () => {
  const m = new Map();
  const st = { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)) };
  let t = 1000;
  const c = LS.createCache(st, { ttl: 100, max: 3, now: () => t });
  c.set('a', [1]);
  assert.deepEqual(c.get('a'), [1]);
  assert.deepEqual(LS.createCache(st, { ttl: 100, now: () => t }).get('a'), [1]);   // sobrevive a reabrir la app
  t += 101;
  assert.equal(c.get('a'), undefined);                                               // caducada
  t += 1; c.set('b', 1); t += 1; c.set('c', 2); t += 1; c.set('d', 3); t += 1; c.set('e', 4);
  assert.equal(c.size(), 3);                                                         // máximo 3: se van las más antiguas
  assert.equal(c.get('b'), undefined);
  assert.equal(c.get('e'), 4);
  c.set('grande', 'x'.repeat(90000));
  assert.equal(c.get('grande'), undefined);                                          // nada enorme
  assert.equal(LS.createCache(st, {}).get('zzz'), undefined);
  m.set('ls.cache', '{ roto');
  assert.equal(LS.createCache(st, {}).size(), 0);                                    // archivo roto: se ignora
});

test('diagnóstico de páginas de YouTube sin vídeos', () => {
  assert.match(LS.diagnoseYouTube(''), /vacía/);
  assert.match(LS.diagnoseYouTube('<a href="https://consent.youtube.com/m">'), /consentimiento/);
  assert.match(LS.diagnoseYouTube('<p>Our systems have detected unusual traffic</p>'), /robot/);
  assert.match(LS.diagnoseYouTube('<html>nada</html>'), /otro formato/);
  assert.match(LS.diagnoseYouTube('var ytInitialData = {};'), /sin vídeos/);
});

test('ajustes: estilo Nsound por defecto, Clásico se conserva y lo raro se corrige', () => {
  const m = new Map();
  const st = { getItem: (k) => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)) };
  const s = LS.createStores(st);
  assert.equal(s.settings.get().look, 'nsound');
  s.settings.set({ look: 'clasico' });
  assert.equal(LS.createStores(st).settings.get().look, 'clasico');
  assert.equal(LS.normalizeSettings({ look: 'raro' }).look, 'nsound');
  for (const [id, name, bg, accent] of LS.LOOKS) {
    assert.equal(LS.normalizeSettings({ look: id }).look, id);
    assert.match(bg, /^#[0-9a-f]{6}$/i); assert.match(accent, /^#[0-9a-f]{6}$/i); assert.ok(name);
  }
  assert.equal(new Set(LS.LOOKS.map((l) => l[0])).size, LS.LOOKS.length);
});

test('YouTube Music: canciones con artista, álbum, duración y carátula', () => {
  const song = (id, title, parts, art) => ({ musicResponsiveListItemRenderer: {
    playlistItemData: { videoId: id },
    thumbnail: { musicThumbnailRenderer: { thumbnail: { thumbnails: [{ url: 'https://lh3.googleusercontent.com/x=w60-h60-l90-rj' }, { url: art }] } } },
    flexColumns: [
      { musicResponsiveListItemFlexColumnRenderer: { text: { runs: [{ text: title }] } } },
      { musicResponsiveListItemFlexColumnRenderer: { text: { runs: parts.flatMap((t, i) => (i ? [{ text: ' • ' }, { text: t }] : [{ text: t }])) } } },
    ] } });
  const json = { contents: { tabbedSearchResultsRenderer: { tabs: [{ tabRenderer: { content: { sectionListRenderer: { contents: [
    { musicShelfRenderer: { contents: [
      song('BSTsnWoslP4', 'Bohemian Rhapsody', ['Queen', 'A Night At The Opera', '5:55'], 'https://lh3.googleusercontent.com/abc=w226-h226-l90-rj'),
      song('2iyV1rsnCzQ', 'Single suelto', ['Artista', '3:30'], ''),
      song('malo', 'Id inválido', ['X', '1:00'], ''),
      song('BSTsnWoslP4', 'Repetida', ['Queen', '5:55'], ''),
    ] } }] } } } }] } } };
  const r = LS.parseYtMusic(JSON.stringify(json));
  assert.equal(r.length, 2);
  assert.deepEqual(r[0], { id: 'BSTsnWoslP4', title: 'Bohemian Rhapsody', channel: 'Queen', album: 'A Night At The Opera', duration: 355, art: 'https://lh3.googleusercontent.com/abc=w544-h544' });
  assert.equal(r[1].album, ''); assert.equal(r[1].duration, 210); assert.equal(r[1].art, '');
  assert.deepEqual(LS.parseYtMusic('{ roto'), []);
  assert.deepEqual(LS.parseYtMusic('{}'), []);
  const body = JSON.parse(LS.musicSearchBody('  queen '));
  assert.equal(body.query, 'queen'); assert.equal(body.params, LS.MUSIC_SONGS_PARAMS); assert.equal(body.context.client.clientName, 'WEB_REMIX');
  assert.equal(LS.genreQuery('Éxitos', 2026), 'éxitos 2026'); assert.equal(LS.genreQuery('Lo-fi', 2026), 'lofi'); assert.equal(LS.genreQuery('Otro', 2026), 'Otro');
  for (const g of LS.GENRES) assert.ok(LS.genreQuery(g, 2026).length > 2, g);
});

test('importar: enlaces válidos y rechazos claros', () => {
  const p = LS.parseImportLink;
  assert.deepEqual([p('https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=x').service, p('https://open.spotify.com/intl-es/track/3zOYmiNOuyDL0ClxJcEJ0T').kind], ['spotify', 'track']);
  assert.equal(p('https://open.spotify.com/artist/06HL4z0CvFAxyc27GXpf02').fetchUrl, 'https://open.spotify.com/embed/artist/06HL4z0CvFAxyc27GXpf02');
  assert.match(p('https://open.spotify.com/album/abc1234567890').error, /álbumes/);
  assert.equal(p('https://www.deezer.com/es/playlist/1313621735').fetchUrl, 'https://api.deezer.com/playlist/1313621735');
  assert.match(p('https://www.deezer.com/es/artist/27').fetchUrl, /\/artist\/27\/top/);
  assert.equal(p('https://youtu.be/dQw4w9WgXcQ').kind, 'track');
  assert.equal(p('https://www.youtube.com/playlist?list=PLabcdef123456').kind, 'playlist');
  assert.match(p('https://www.youtube.com/@canal').error, /canales/);
  assert.equal(p('https://music.apple.com/es/playlist/x/pl.abc').kind, 'playlist');
  assert.match(p('https://music.apple.com/es/album/x/123').error, /álbumes/);
  assert.equal(p('https://soundcloud.com/forss/flickermood').kind, 'track');
  assert.match(p('https://soundcloud.com/forss/sets/s').error, /SoundCloud/);
  assert.match(p('hola').error, /enlace/);
  assert.match(p('https://example.com/x').error, /no compatible/);
  assert.match(p('javascript:alert(1)').error, /enlace/);
});

test('importar: lectura de Spotify, Deezer, YouTube y SoundCloud', () => {
  const sp = '<script id="__NEXT_DATA__" type="application/json">' + JSON.stringify({ props: { pageProps: { state: { data: { entity: { name: 'Mi lista', trackList: [{ title: 'A', subtitle: 'X' }, { title: 'A', subtitle: 'X' }, { title: 'B', subtitle: 'Y' }] } } } } } }) + '</script>';
  const r = LS.parseImported({ service: 'spotify', kind: 'playlist' }, sp);
  assert.equal(r.name, 'Mi lista'); assert.deepEqual(r.tracks, [{ title: 'A', artist: 'X' }, { title: 'B', artist: 'Y' }]);
  const dz = LS.parseImported({ service: 'deezer', kind: 'playlist' }, JSON.stringify({ title: 'L', tracks: { data: [{ title: 'T', artist: { name: 'Z' } }] } }));
  assert.deepEqual(dz.tracks, [{ title: 'T', artist: 'Z' }]);
  const yt = 'var ytInitialData = ' + JSON.stringify({ a: { playlistHeaderRenderer: { title: { simpleText: 'PL' } }, b: [{ playlistVideoRenderer: { title: { runs: [{ text: 'Song' }] }, shortBylineText: { runs: [{ text: 'Band - Topic' }] } } }] } }) + ';</script>';
  assert.deepEqual(LS.parseImported({ service: 'youtube', kind: 'playlist' }, yt), { name: 'PL', tracks: [{ title: 'Song', artist: 'Band' }] });
  assert.deepEqual(LS.parseImported({ service: 'soundcloud', kind: 'track' }, JSON.stringify({ title: 'Flickermood by Forss', author_name: 'Forss' })).tracks, [{ title: 'Flickermood', artist: 'Forss' }]);
  assert.deepEqual(LS.parseImported({ service: 'spotify', kind: 'track' }, 'basura').tracks, []);
  assert.equal(LS.parseImported({ service: 'deezer', kind: 'playlist' }, JSON.stringify({ title: 'L', tracks: { data: Array.from({ length: 300 }, (_, i) => ({ title: 't' + i, artist: { name: 'a' } })) } })).tracks.length, LS.IMPORT_MAX);
});

test('importar: elegir la canción correcta', () => {
  const res = [{ title: 'Loser (Live)', channel: 'Otro grupo' }, { title: 'Loser', channel: 'Tame Impala' }];
  assert.equal(LS.pickImportMatch({ title: 'Loser', artist: 'Tame Impala' }, res), res[1]);
  assert.equal(LS.pickImportMatch({ title: 'Inexistente', artist: 'Nadie' }, res), null);
});

test('cola: añadir, quitar, mover y vaciar', () => {
  const k = (n) => ({ key: 'k' + n });
  const q = [k(1), k(2), k(3)];
  let r = LS.queueAdd(q, 0, k(9), 'next');
  assert.deepEqual(r.queue.map((x) => x.key), ['k1', 'k9', 'k2', 'k3']); assert.equal(r.index, 0);
  r = LS.queueAdd(r.queue, 0, k(9), 'end');                       // ya estaba: se mueve, sin duplicados
  assert.deepEqual(r.queue.map((x) => x.key), ['k1', 'k2', 'k3', 'k9']);
  r = LS.queueAdd(q, 1, k(1), 'next');                            // una anterior que se vuelve a pedir: pasa a continuación
  assert.deepEqual(r.queue.map((x) => x.key), ['k2', 'k1', 'k3']); assert.equal(r.index, 0);
  assert.deepEqual(LS.queueAdd(q, 1, k(2), 'end').queue.map((x) => x.key), ['k1', 'k2', 'k3']);   // la actual no se duplica
  assert.deepEqual(LS.queueAdd([], -1, k(5), 'next'), { queue: [k(5)], index: 0 });
  r = LS.queueRemove(q, 1, 0); assert.deepEqual([r.queue.length, r.index], [2, 0]);   // quitar una anterior reajusta el índice
  assert.equal(LS.queueRemove(q, 1, 1).queue.length, 3);          // la actual no se quita
  assert.deepEqual(LS.queueMove(q, 0, 2, -1).queue.map((x) => x.key), ['k1', 'k3', 'k2']);
  assert.deepEqual(LS.queueMove(q, 0, 1, -1).queue.map((x) => x.key), ['k1', 'k2', 'k3']);        // no pasa por encima de la actual
  assert.deepEqual(LS.queueClearUpcoming(q, 1).queue.map((x) => x.key), ['k1', 'k2']);
});

test('pantalla de bloqueo: artista, álbum y carátula limpios', () => {
  const m = LS.mediaMeta({ src: 'yt', title: 'Q Hubo Amor', artist: 'Ovy On The Drums', sub: 'Ovy On The Drums · Q Hubo Amor (Medellín)', thumb: 'https://lh3.googleusercontent.com/x=w544-h544' });
  assert.deepEqual([m.title, m.artist, m.album, m.artwork[0].sizes], ['Q Hubo Amor', 'Ovy On The Drums', 'Q Hubo Amor (Medellín)', '544x544']);
  const v = LS.mediaMeta({ src: 'yt', title: 'Video', artist: 'Canal - Topic', sub: 'Canal - Topic', thumb: 'https://i.ytimg.com/vi/abc/mqdefault.jpg' });
  assert.deepEqual([v.artist, v.album, v.artwork[0].sizes], ['Canal', '', '320x180']);
  assert.deepEqual(LS.mediaMeta({ title: 'x', thumb: 'http://inseguro/x.jpg' }).artwork, []);
  assert.equal(LS.mediaMeta(null).title, '');
  assert.deepEqual(LS.mediaPosition(5, 100), { duration: 100, position: 5, playbackRate: 1 });
  assert.equal(LS.mediaPosition(5, 0), null);
  assert.equal(LS.mediaPosition(500, 100).position, 100);
  assert.equal(LS.mediaPosition(NaN, 100), null);
});

test('color de la carátula y pulso', () => {
  const solid = (r, g, b, n = 50) => Array.from({ length: n }, () => [r, g, b, 255]).flat();
  assert.ok(Math.abs(LS.coverColor(solid(220, 20, 20)).h - 0) <= 1 || LS.coverColor(solid(220, 20, 20)).h >= 359);
  assert.ok(Math.abs(LS.coverColor(solid(20, 40, 220)).h - 232) <= 8);
  assert.equal(LS.coverColor(solid(128, 128, 128)).h, null);                 // gris: sin color
  assert.equal(LS.coverColor(solid(5, 5, 5)).h, null);                       // casi negro (barras): sin color
  const mix = [...solid(20, 200, 40, 40), ...solid(120, 120, 120, 200), ...solid(0, 0, 0, 200)];
  const c = LS.coverColor(mix);
  assert.ok(c.h > 100 && c.h < 140 && c.s >= 40 && c.s <= 85 && c.l >= 28 && c.l <= 44, JSON.stringify(c));   // domina el verde
  assert.equal(LS.coverColor([]).h, null);
  for (let t = 0; t < 10; t += 0.37) { const e = LS.pulseAt(t, 7); assert.ok(e >= 0 && e <= 1); }
  assert.equal(LS.pulseAt(1.23, 5), LS.pulseAt(1.23, 5));                    // estable por canción
  assert.equal(LS.normalizeSettings({}).reactive, true);
  assert.equal(LS.normalizeSettings({ reactive: false }).reactive, false);
});
