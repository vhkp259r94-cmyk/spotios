/* Ajustes, biblioteca, listas y descargas. Se guardan en localStorage (o en cualquier objeto con getItem/setItem). */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const PREFIX = 'ls.';

  function read(storage, name, fallback) {
    try {
      const raw = storage.getItem(PREFIX + name);
      return raw === null || raw === undefined ? fallback : JSON.parse(raw);
    } catch (e) { return fallback; }
  }

  function write(storage, name, value) {
    try { storage.setItem(PREFIX + name, JSON.stringify(value)); } catch (e) { /* sin espacio o bloqueado */ }
  }

  /** Corrige ajustes con forma inesperada (archivo antiguo o editado a mano). */
  LS.normalizeSettings = function (s) {
    s = s && typeof s === 'object' ? s : {};
    const bands = Array.from({ length: 10 }, (_, i) => {
      const v = Array.isArray(s.eqBands) ? Number(s.eqBands[i]) : 0;
      return Number.isFinite(v) ? Math.max(-12, Math.min(12, Math.round(v))) : 0;
    });
    const volume = Number.isFinite(Number(s.volume)) ? Math.max(0, Math.min(100, Number(s.volume))) : 80;
    let preset = typeof s.eqPreset === 'string' ? s.eqPreset : 'Plano';
    if (preset !== LS.EQ_CUSTOM && !(preset in LS.EQ_PRESETS)) preset = LS.EQ_CUSTOM;
    return { eqEnabled: !!s.eqEnabled, eqPreset: preset, eqBands: bands, adBlock: s.adBlock !== false, volume, look: s.look === 'clasico' ? 'clasico' : 'nsound' };
  };

  /**
   * Caché con caducidad para resultados ya analizados (unos pocos KB cada uno, no las páginas de 1-2 MB de YouTube).
   * Evita descargar lo mismo en cada arranque: ahorra datos, batería y calor.
   */
  LS.createCache = function (storage, opts) {
    opts = opts || {};
    const ttl = opts.ttl || 30 * 60 * 1000, max = opts.max || 24, name = 'cache';
    const now = opts.now || (() => Date.now());
    let data = read(storage, name, {});
    if (!data || typeof data !== 'object' || Array.isArray(data)) data = {};
    const persist = () => write(storage, name, data);
    return {
      get(key) {
        const e = data[key];
        if (!e || typeof e.t !== 'number' || now() - e.t > ttl) return undefined;
        return e.v;
      },
      set(key, value) {
        if (JSON.stringify(value).length > 80000) return;      // nada enorme en el almacenamiento del teléfono
        data[key] = { t: now(), v: value };
        const keys = Object.keys(data).sort((x, y) => data[x].t - data[y].t);
        for (const k of keys.slice(0, Math.max(0, keys.length - max))) delete data[k];   // se queda con los más recientes
        persist();
      },
      size: () => Object.keys(data).length,
    };
  };

  LS.createStores = function (storage) {
    const itemKey = (i) => i.src + ':' + i.id;
    // De cada elemento solo se guarda lo necesario para volver a mostrarlo y reproducirlo
    const slim = (i) => ({
      src: i.src, id: i.id, title: i.title, sub: i.sub || '', duration: i.duration || 0,
      thumb: i.thumb || '', url: i.url || '', kind: i.kind || '', published: i.published || null,
    });

    let settings = LS.normalizeSettings(read(storage, 'settings', {}));
    let library = read(storage, 'library', []).filter((i) => i && i.id && i.src);
    let playlists = read(storage, 'playlists', []).filter((p) => p && p.id && Array.isArray(p.items));
    let downloads = read(storage, 'downloads', []).filter((d) => d && d.id && d.url);

    return {
      settings: {
        get: () => settings,
        set(patch) { settings = LS.normalizeSettings(Object.assign({}, settings, patch)); write(storage, 'settings', settings); return settings; },
      },
      library: {
        items: () => library,
        has: (item) => library.some((i) => itemKey(i) === itemKey(item)),
        /** Guarda o quita. Devuelve true si queda guardado. */
        toggle(item) {
          const k = itemKey(item), had = library.some((i) => itemKey(i) === k);
          library = had ? library.filter((i) => itemKey(i) !== k) : [slim(item), ...library];
          write(storage, 'library', library);
          return !had;
        },
      },
      playlists: {
        all: () => playlists,
        find: (id) => playlists.find((p) => p.id === id) || null,
        create(name) {
          const p = { id: Math.random().toString(36).slice(2, 10), name: String(name).trim().slice(0, 60) || 'Lista', items: [] };
          playlists = [...playlists, p];
          write(storage, 'playlists', playlists);
          return p;
        },
        remove(id) { playlists = playlists.filter((p) => p.id !== id); write(storage, 'playlists', playlists); },
        has: (id, item) => !!(playlists.find((p) => p.id === id) || { items: [] }).items.some((i) => itemKey(i) === itemKey(item)),
        /** Añade o quita de la lista. Devuelve true si queda dentro. */
        toggle(id, item) {
          const p = playlists.find((x) => x.id === id);
          if (!p) return false;
          const k = itemKey(item), had = p.items.some((i) => itemKey(i) === k);
          p.items = had ? p.items.filter((i) => itemKey(i) !== k) : [...p.items, slim(item)];
          write(storage, 'playlists', playlists);
          return !had;
        },
      },
      downloads: {
        all: () => downloads,
        find: (id) => downloads.find((d) => d.id === id) || null,
        add(d) { downloads = [d, ...downloads.filter((x) => x.id !== d.id)]; write(storage, 'downloads', downloads); },
        remove(id) { downloads = downloads.filter((d) => d.id !== id); write(storage, 'downloads', downloads); },
      },
    };
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
