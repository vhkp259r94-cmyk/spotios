using System.Text.Json;

namespace LibreStream.Core;

/// <summary>Elemento guardado por el usuario. Source: "catalog" o "youtube".</summary>
public sealed record SavedItem(string Source, string Id, string Title, string Subtitle, double DurationSec, string? ArtworkUrl);

/// <summary>Biblioteca personal persistida en un JSON (library.json) dentro de <paramref name="dir"/>.</summary>
public sealed class LibraryStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly List<SavedItem> _items;

    public LibraryStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "library.json");
        try
        {
            _items = File.Exists(_file)
                ? JsonSerializer.Deserialize<List<SavedItem>>(File.ReadAllText(_file), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _items = [];
        }
    }

    public IReadOnlyList<SavedItem> Items => _items;

    public bool Contains(string source, string id) => _items.Any(i => i.Source == source && i.Id == id);

    /// <summary>Guarda o quita el elemento. Devuelve true si queda guardado.</summary>
    public bool Toggle(SavedItem item)
    {
        var removed = _items.RemoveAll(i => i.Source == item.Source && i.Id == item.Id) > 0;
        if (!removed) _items.Insert(0, item);
        File.WriteAllText(_file, JsonSerializer.Serialize(_items, Json));
        return !removed;
    }
}
