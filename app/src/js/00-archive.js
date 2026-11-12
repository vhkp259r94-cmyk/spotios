/* Música libre de Internet Archive (licencias Creative Commons y netlabels). Sin cuenta ni clave; reproducción propia con HTML5. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';
  const BASE = 'https://archive.org';

  // Solo contenido con licencia libre: Creative Commons o colección de netlabels
  LS.archiveSearchUrl = function (query, rows) {
    const q = String(query || '').replace(/["()\:\[\]{}^~*?]/g, ' ').replace(/\s+/g, ' ').trim();
    const filter = 'mediatype:audio AND (licenseurl:*creativecommons* OR collection:netlabels)';
    const p = ['q=' + encodeURIComponent((q ? '(' + q + ') AND ' : '') + filter),
      'fl%5B%5D=identifier', 'fl%5B%5D=title', 'fl%5B%5D=creator', 'fl%5B%5D=licenseurl',
      'sort%5B%5D=downloads+desc', 'rows=' + (rows || 12), 'output=json'];
    return BASE + '/advancedsearch.php?' + p.join('&');
  };

  const text = (v) => String(Array.isArray(v) ? v[0] : (v == null ? '' : v)).trim();

  LS.parseArchiveSearch = function (json) {
    const docs = json && json.response && Array.isArray(json.response.docs) ? json.response.docs : [];
    return docs.filter((d) => d && d.identifier && /^[A-Za-z0-9._-]+$/.test(d.identifier)).map((d) => ({
      id: d.identifier, title: text(d.title) || d.identifier, artist: text(d.creator) || 'Internet Archive', license: text(d.licenseurl),
    }));
  };

  // Elige el primer archivo de audio reproducible (MP3 u Ogg) de un elemento
  LS.archivePickFile = function (id, meta) {
    const files = meta && Array.isArray(meta.files) ? meta.files : [];
    const ok = files.filter((f) => f && typeof f.name === 'string' && /\.(mp3|ogg|oga)$/i.test(f.name) && f.source !== 'metadata');
    const f = ok.find((x) => /mp3/i.test(x.format || '') || /\.mp3$/i.test(x.name)) || ok[0];
    if (!f) return null;
    const len = String(f.length || '');
    const secs = len.includes(':') ? len.split(':').reduce((a, n) => a * 60 + (parseFloat(n) || 0), 0) : parseFloat(len) || 0;
    return { url: BASE + '/download/' + encodeURIComponent(id) + '/' + f.name.split('/').map(encodeURIComponent).join('/'), duration: Math.round(secs) };
  };

  LS.archiveThumb = (id) => BASE + '/services/img/' + encodeURIComponent(id);

  LS.archiveSearch = async function (query, fetchJson) {
    return LS.parseArchiveSearch(await fetchJson(LS.archiveSearchUrl(query)));
  };
  LS.archiveResolve = async function (id, fetchJson) {
    const r = LS.archivePickFile(id, await fetchJson(BASE + '/metadata/' + encodeURIComponent(id)));
    if (!r) throw new Error('Este elemento no tiene audio reproducible');
    return r;
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
