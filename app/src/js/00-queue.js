/* Cola de reproducción: operaciones puras sobre { queue, index } (la canción actual es queue[index]).
   Siempre devuelven un objeto nuevo; nunca modifican el original. */
var LS = globalThis.LS = globalThis.LS || {};
(function (LS) {
  'use strict';

  const valid = (q, i) => Array.isArray(q) && Number.isInteger(i) && i >= 0 && i < q.length;

  /** mode 'next': justo después de la actual · 'end': al final. Sin duplicados: si ya estaba más adelante, se mueve. */
  LS.queueAdd = function (queue, index, item, mode) {
    if (!item || !item.key) return { queue: queue || [], index: index || 0 };
    if (!valid(queue, index)) return { queue: [item], index: 0 };
    if (queue[index].key === item.key) return { queue: queue.slice(), index };
    const cur = queue[index];
    const rest = queue.filter((x, i) => i === index || x.key !== item.key);
    const at = rest.indexOf(cur);
    if (mode === 'next') rest.splice(at + 1, 0, item); else rest.push(item);
    return { queue: rest, index: at };
  };

  /** Quita la canción en `pos` (no la actual). */
  LS.queueRemove = function (queue, index, pos) {
    if (!valid(queue, index) || !valid(queue, pos) || pos === index) return { queue: (queue || []).slice(), index };
    return { queue: queue.filter((x, i) => i !== pos), index: pos < index ? index - 1 : index };
  };

  /** Sube (dir -1) o baja (dir +1) una canción de las que vienen después de la actual. */
  LS.queueMove = function (queue, index, pos, dir) {
    const to = pos + dir;
    if (!valid(queue, index) || pos <= index || to <= index || to >= queue.length) return { queue: (queue || []).slice(), index };
    const q = queue.slice();
    [q[pos], q[to]] = [q[to], q[pos]];
    return { queue: q, index };
  };

  /** Vacía lo que viene después de la actual. */
  LS.queueClearUpcoming = function (queue, index) {
    return valid(queue, index) ? { queue: queue.slice(0, index + 1), index } : { queue: [], index: 0 };
  };
})(LS);
if (typeof module !== 'undefined' && module.exports) module.exports = LS;
