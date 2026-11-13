using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

/// <summary>
/// Importar una lista desde un archivo que elige el usuario (sin cuentas ni contraseñas). Admite el CSV de Exportify (Spotify),
/// el JSON de «Descargar tus datos» de Spotify, un JSON genérico y un texto con «Artista - Título» por línea.
/// Mismas reglas que la web y los móviles (app/src/js/00-import.js). Solo se leen títulos y artistas.
/// </summary>
public static partial class FileImport
{
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@" [-–—] ")] private static partial Regex Dash();

    private static string Clean(string? s) => Spaces().Replace(s ?? "", " ").Trim();

    private static ImportTrack? T(string? title, string? artist)
    {
        var t = Clean(title);
        return t.Length is > 0 and <= 200 ? new ImportTrack(t, Clean(artist)) : null;
    }

    private static string FirstArtist(string? s) => Clean((s ?? "").Split(';', '|')[0]);

    /// <summary>Contenido del archivo → listas (nombre + canciones sin repetir, hasta <see cref="LinkImport.Max"/>), o un mensaje de error claro.</summary>
    public static (List<ImportInfo>? Lists, string? Error) Parse(string fileName, string text)
    {
        var raw = (text ?? "").TrimStart('﻿');
        var baseName = Clean(Path.GetFileNameWithoutExtension(fileName));
        if (baseName.Length == 0) baseName = "Lista importada";

        (List<ImportInfo>?, string?) Finish(IEnumerable<(string? Name, IEnumerable<ImportTrack?> Tracks)> lists)
        {
            var output = new List<ImportInfo>();
            foreach (var (name, tracks) in lists)
            {
                var seen = new HashSet<string>();
                var all = tracks.OfType<ImportTrack>().Where(t => seen.Add((t.Title + "|" + t.Artist).ToLowerInvariant())).Take(LinkImport.Max).ToList();
                if (all.Count == 0) continue;
                var n = Clean(name);
                output.Add(new ImportInfo(n.Length == 0 ? baseName : n[..Math.Min(60, n.Length)], all));
            }
            return output.Count > 0 ? (output, null) : (null, "No encontré canciones en ese archivo.");
        }

        if (raw.TrimStart().StartsWith('[') || raw.TrimStart().StartsWith('{'))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); }
            catch (JsonException) { return (null, "El archivo JSON no se puede leer."); }
            using (doc)
            {
                var root = doc.RootElement;
                string? S(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                // «Descargar tus datos» de Spotify: { playlists: [{ name, items: [{ track: { trackName, artistName } }] }] }
                var lists = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("playlists", out var p) && p.ValueKind == JsonValueKind.Array ? p
                    : root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Object && root[0].TryGetProperty("items", out _) ? root
                    : (JsonElement?)null;
                if (lists is { } l && l.ValueKind == JsonValueKind.Array)
                    return Finish(l.EnumerateArray().Select(pl => (S(pl, "name"),
                        pl.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                            ? items.EnumerateArray().Select(it => it.ValueKind == JsonValueKind.Object && it.TryGetProperty("track", out var tr) && tr.ValueKind == JsonValueKind.Object
                                ? T(S(tr, "trackName"), S(tr, "artistName")) : null).ToList()
                            : new List<ImportTrack?>().AsEnumerable())).ToList());

                // JSON genérico: lista de canciones, o { name, tracks | items: [...] }
                JsonElement? arr = root.ValueKind == JsonValueKind.Array ? root
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tracks", out var tk) && tk.ValueKind == JsonValueKind.Array ? tk
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var it2) && it2.ValueKind == JsonValueKind.Array ? it2
                    : null;
                if (arr is { } a)
                    return Finish([(S(root, "name"), a.EnumerateArray().Select(x =>
                    {
                        var title = S(x, "title") ?? S(x, "name") ?? S(x, "trackName");
                        var artist = S(x, "artist") ?? S(x, "artistName");
                        if (artist is null && x.ValueKind == JsonValueKind.Object && x.TryGetProperty("artists", out var ar) && ar.ValueKind == JsonValueKind.Array && ar.GetArrayLength() > 0)
                            artist = ar[0].ValueKind == JsonValueKind.String ? ar[0].GetString() : S(ar[0], "name");
                        return T(title, artist);
                    }).ToList().AsEnumerable())]);
                return (null, "No reconozco ese JSON. Usa el de «Descargar tus datos» de Spotify.");
            }
        }

        var first = raw.Split('\n', 2)[0].TrimEnd('\r');
        var delim = first.Contains(',') || !first.Contains(';') ? ',' : ';';
        if (first.Contains(delim) && Regex.IsMatch(first, "track|title|song|canci|name", RegexOptions.IgnoreCase))
        {
            var rows = ParseCsv(raw, delim);
            var head = rows[0].Select(h => Clean(h).ToLowerInvariant()).ToList();
            int Find(params string[] names) => head.FindIndex(h => names.Any(n => h == n || h.StartsWith(n, StringComparison.Ordinal)));
            var ti = Find("track name", "trackname", "title", "song", "canción", "cancion", "name");
            var ai = Find("artist name", "artistname", "artist", "artista", "artists");
            if (ti >= 0)
                return Finish([(null, rows.Skip(1).Select(r => T(r.ElementAtOrDefault(ti), ai >= 0 ? FirstArtist(r.ElementAtOrDefault(ai)) : "")).ToList().AsEnumerable())]);
        }

        var lines = raw.Split('\n').Select(Clean).Where(x => x.Length > 0).ToList();
        var withDash = lines.Where(x => Dash().IsMatch(x)).ToList();
        if (withDash.Count >= Math.Max(1, (int)Math.Floor(lines.Count * 0.6)))
            return Finish([(null, withDash.Select(x =>
            {
                var m = Dash().Match(x);
                return T(x[(m.Index + m.Length)..], x[..m.Index]);
            }).ToList().AsEnumerable())]);
        return (null, "No reconozco el formato. Usa un CSV de Exportify, el JSON de Spotify o un texto con «Artista - Título» por línea.");
    }

    /// <summary>CSV (RFC 4180) → filas. <paramref name="delim"/>: ',' o ';'.</summary>
    public static List<List<string>> ParseCsv(string text, char delim = ',')
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        var t = text.TrimStart('﻿');
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < t.Length && t[i + 1] == '"') { cur.Append('"'); i++; } else quoted = false; }
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delim) { row.Add(cur.ToString()); cur.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < t.Length && t[i + 1] == '\n') i++;
                row.Add(cur.ToString()); cur.Clear();
                if (row.Any(x => x.Length > 0)) rows.Add(row);
                row = [];
            }
            else cur.Append(c);
        }
        row.Add(cur.ToString());
        if (row.Any(x => x.Length > 0)) rows.Add(row);
        return rows;
    }
}
