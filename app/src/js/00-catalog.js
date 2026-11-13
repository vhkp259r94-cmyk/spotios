/* Catálogo con APIs gratuitas: MusicBrainz (fichas y discografía) y Cover Art Archive (portadas). Sin cuenta ni clave.
   Solo da información y portadas; el audio sigue saliendo de YouTube. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';
  const MB = 'https://musicbrainz.org/ws/2';

  // Nombre sin tildes, mayúsculas ni signos, para comparar «Rosalía» con «ROSALIA»
  LS.normName = (s) => String(s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '');

  // En YouTube Music el canal suele llamarse «Artista - Topic» o «ArtistaVEVO»
  LS.cleanArtistName = (s) => String(s || '').replace(/\s*-\s*Topic$/i, '').replace(/VEVO$/i, '').replace(/\s+/g, ' ').trim();

  LS.artistSearchUrl = (name) => MB + '/artist/?query=' + encodeURIComponent('artist:"' + String(name).replace(/["\\]/g, ' ').trim() + '"') + '&fmt=json&limit=6';

  // Elige el artista que coincide exactamente con el nombre (el de más puntuación). Si no hay coincidencia clara devuelve null:
  // para artistas pequeños o nuevos la página se muestra solo con sus canciones, sin inventar una ficha equivocada.
  LS.pickArtist = function (json, name) {
    const list = json && Array.isArray(json.artists) ? json.artists : [];
    const want = LS.normName(name);
    if (!want) return null;
    const exact = list.filter((a) => a && a.id && (LS.normName(a.name) === want || (a['sort-name'] && LS.normName(a['sort-name']) === want)));
    if (!exact.length) return null;
    exact.sort((a, b) => (b.score || 0) - (a.score || 0) || ((b.tags || []).length - (a.tags || []).length));
    const a = exact[0];
    if ((a.score || 0) < 80) return null;
    const tags = (a.tags || []).slice().sort((x, y) => (y.count || 0) - (x.count || 0)).slice(0, 4).map((t) => t.name);
    const begin = a['life-span'] && a['life-span'].begin ? String(a['life-span'].begin).slice(0, 4) : '';
    return {
      mbid: a.id, name: a.name, type: a.type || '', country: (a.area && a.area.name) || '', begin, tags,
      note: a.disambiguation || '',
    };
  };

  LS.releaseGroupsUrl = (mbid) => MB + '/release-group?artist=' + encodeURIComponent(mbid) + '&type=album%7Cep&fmt=json&limit=100';

  // Discos de estudio (sin directos, recopilatorios ni remixes), sin repetir título, los más recientes primero
  LS.parseReleaseGroups = function (json) {
    const list = json && Array.isArray(json['release-groups']) ? json['release-groups'] : [];
    const seen = new Set(), out = [];
    for (const r of list) {
      if (!r || !r.id || !r.title) continue;
      if (Array.isArray(r['secondary-types']) && r['secondary-types'].length) continue;
      const key = LS.normName(r.title);
      if (seen.has(key)) continue;
      seen.add(key);
      out.push({ id: r.id, title: r.title, type: r['primary-type'] || 'Album', date: r['first-release-date'] || '', year: String(r['first-release-date'] || '').slice(0, 4) });
    }
    out.sort((a, b) => (b.date || '').localeCompare(a.date || ''));
    return out;
  };

  // La imagen se pide directamente (el servidor redirige); si no hay portada el <img> falla y se oculta
  LS.coverUrl = (rgid, size) => 'https://coverartarchive.org/release-group/' + encodeURIComponent(rgid) + '/front-' + (size || 250);

  // Dirección interna de la página de artista
  LS.artistHash = (name) => '#/artist/' + encodeURIComponent(name);
  LS.artistFromParts = (parts) => { try { return decodeURIComponent(parts.slice(1).join('/')).trim(); } catch (e) { return ''; } };

  // MusicBrainz pide como mucho 1 petición por segundo: cola con espera entre llamadas
  LS.makeLimiter = function (gapMs, now, wait) {
    const clock = now || Date.now, sleep = wait || ((ms) => new Promise((r) => setTimeout(r, ms)));
    let last = 0, chain = Promise.resolve();
    return (fn) => {
      const run = chain.then(async () => {
        const gap = last + gapMs - clock();
        if (gap > 0) await sleep(gap);
        try { return await fn(); } finally { last = clock(); }
      });
      chain = run.catch(() => {});
      return run;
    };
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
