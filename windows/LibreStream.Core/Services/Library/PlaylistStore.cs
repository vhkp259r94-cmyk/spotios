using System.Text.Json;

namespace LibreStream.Core;

public sealed record Playlist(string Id, string Name, List<SavedItem> Items);

/// <summary>Listas de reproducción del usuario, persistidas en playlists.json dentro de <c>dir</c>.</summary>
public sealed class PlaylistStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly List<Playlist> _lists;

    public PlaylistStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "playlists.json");
        try
        {
            _lists = File.Exists(_file)
                ? JsonSerializer.Deserialize<List<Playlist>>(File.ReadAllText(_file), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _lists = [];
        }
    }

    public IReadOnlyList<Playlist> All => _lists;

    public Playlist? Find(string id) => _lists.FirstOrDefault(p => p.Id == id);

    public Playlist Create(string name)
    {
        var list = new Playlist(Guid.NewGuid().ToString("N")[..8], name.Trim(), []);
        _lists.Add(list);
        Save();
        return list;
    }

    public void Delete(string id)
    {
        if (_lists.RemoveAll(p => p.Id == id) > 0) Save();
    }

    public bool Contains(string id, SavedItem item) =>
        Find(id)?.Items.Any(i => i.Source == item.Source && i.Id == item.Id) == true;

    /// <summary>Añade varias canciones de una vez (una sola escritura) sin repetir. Devuelve cuántas añadió.</summary>
    public int AddMany(string id, IEnumerable<SavedItem> items)
    {
        if (Find(id) is not { } list) return 0;
        var have = list.Items.Select(i => (i.Source, i.Id)).ToHashSet();
        var added = 0;
        foreach (var it in items)
            if (have.Add((it.Source, it.Id))) { list.Items.Add(it); added++; }
        if (added > 0) Save();
        return added;
    }

    /// <summary>Añade o quita el elemento de la lista. Devuelve true si queda dentro.</summary>
    public bool Toggle(string id, SavedItem item)
    {
        if (Find(id) is not { } list) return false;
        var removed = list.Items.RemoveAll(i => i.Source == item.Source && i.Id == item.Id) > 0;
        if (!removed) list.Items.Add(item);
        Save();
        return !removed;
    }

    private void Save() => File.WriteAllText(_file, JsonSerializer.Serialize(_lists, Json));
}
