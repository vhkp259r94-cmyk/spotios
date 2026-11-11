using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>Elemento del catálogo libre descargado al disco para verlo sin conexión.</summary>
public sealed record DownloadedItem(string Id, string Title, string Artist, string Kind, string FilePath,
    long Bytes, double DurationSec, string? ArtworkUrl);

/// <summary>Registro de descargas (downloads.json). Solo recuerda; los ficheros viven en la carpeta elegida.</summary>
public sealed class DownloadStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;
    private readonly List<DownloadedItem> _items;

    public DownloadStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "downloads.json");
        try
        {
            _items = File.Exists(_file)
                ? JsonSerializer.Deserialize<List<DownloadedItem>>(File.ReadAllText(_file), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _items = [];
        }
        // Olvida lo que el usuario haya borrado a mano del disco
        if (_items.RemoveAll(i => !File.Exists(i.FilePath)) > 0) Save();
    }

    public IReadOnlyList<DownloadedItem> Items => _items;

    public DownloadedItem? Find(string id) => _items.FirstOrDefault(i => i.Id == id);

    public void Add(DownloadedItem item)
    {
        _items.RemoveAll(i => i.Id == item.Id);
        _items.Insert(0, item);
        Save();
    }

    /// <summary>Quita del registro y borra el fichero descargado.</summary>
    public void Remove(string id)
    {
        if (Find(id) is not { } item) return;
        _items.Remove(item);
        try { File.Delete(item.FilePath); } catch (IOException) { /* en uso: se queda en disco */ }
        Save();
    }

    private void Save() => File.WriteAllText(_file, JsonSerializer.Serialize(_items, Json));
}

public static partial class FileDownloader
{
    [GeneratedRegex(@"[^\p{L}\p{N} ._()\-]+")]
    private static partial Regex Unsafe();

    /// <summary>Nombre de fichero seguro para Windows a partir de un título.</summary>
    public static string SafeFileName(string title, string extension)
    {
        var name = Unsafe().Replace(title, " ");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '.');
        if (name.Length == 0) name = "descarga";
        if (name.Length > 80) name = name[..80].TrimEnd();
        return name + (extension.StartsWith('.') ? extension : "." + extension);
    }

    /// <summary>
    /// Descarga <paramref name="url"/> (solo https) a <paramref name="destination"/> con un fichero .part que
    /// se renombra al terminar; si falla o se cancela, no deja restos. Devuelve los bytes escritos.
    /// </summary>
    public static async Task<long> DownloadAsync(HttpClient http, Uri url, string destination,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Solo se admiten descargas https.", nameof(url));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";
        try
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            long written = 0;
            await using (var input = await resp.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)written / total));
                }
            }
            File.Move(part, destination, overwrite: true);
            progress?.Report(1.0);
            return written;
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { }
            throw;
        }
    }
}
