import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
require('../src/js/00-core.js');
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
