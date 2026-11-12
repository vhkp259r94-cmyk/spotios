namespace LibreStream.Core;

/// <summary>
/// Operaciones de la cola de reproducción (la canción actual es <c>queue[index]</c>). Mismas reglas que la interfaz web
/// (<c>app/src/js/00-queue.js</c>): sin duplicados, y la canción actual nunca se quita ni se mueve.
/// </summary>
public static class QueueOps
{
    /// <summary>Añade justo después de la actual (<paramref name="next"/>) o al final. Si ya estaba más adelante, se mueve.</summary>
    public static void Add<T>(List<T> queue, ref int index, T item, bool next, Func<T, T, bool> same)
    {
        if (index < 0 || index >= queue.Count) { queue.Clear(); queue.Add(item); index = 0; return; }
        var current = queue[index];
        if (same(current, item)) return;
        for (var i = queue.Count - 1; i >= 0; i--)
            if (i != index && same(queue[i], item)) { queue.RemoveAt(i); if (i < index) index--; }
        if (next) queue.Insert(index + 1, item); else queue.Add(item);
    }

    /// <summary>Quita la canción en <paramref name="pos"/> (nunca la actual).</summary>
    public static bool Remove<T>(List<T> queue, ref int index, int pos)
    {
        if (index < 0 || pos < 0 || pos >= queue.Count || pos == index) return false;
        queue.RemoveAt(pos);
        if (pos < index) index--;
        return true;
    }

    /// <summary>Sube (-1) o baja (+1) una canción de las que vienen después de la actual.</summary>
    public static bool Move<T>(List<T> queue, int index, int pos, int dir)
    {
        var to = pos + dir;
        if (index < 0 || pos <= index || to <= index || to >= queue.Count || pos >= queue.Count) return false;
        (queue[pos], queue[to]) = (queue[to], queue[pos]);
        return true;
    }

    /// <summary>Vacía lo que viene después de la actual.</summary>
    public static void ClearUpcoming<T>(List<T> queue, int index)
    {
        if (index >= 0 && index < queue.Count - 1) queue.RemoveRange(index + 1, queue.Count - index - 1);
    }
}
