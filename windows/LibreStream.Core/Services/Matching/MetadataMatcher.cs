using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LibreStream.Core;

public enum TrackVersion { Original, Remix, Live, Acoustic, Instrumental, Cover, Edit }

public sealed record CleanTitle(string Title, IReadOnlyList<string> Featuring, TrackVersion Version)
{
    public bool Equals(CleanTitle? o) =>
        o is not null && Title == o.Title && Version == o.Version && Featuring.SequenceEqual(o.Featuring);
    public override int GetHashCode() => HashCode.Combine(Title, Version, Featuring.Count);
}

/// <summary>
/// Normaliza títulos, compara duraciones (±4 s) y genera la clave MD5(artista|título|versión).
/// Patrones idénticos a tools/title_normalizer.py e ios/.../MetadataMatcher.swift.
/// </summary>
public static class MetadataMatcher
{
    public const double DurationTolerance = 4.0;
    private const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Noise = new(
        @"\s*[\(\[\{][^\)\]\}]*?\b(official|oficial|video|vídeo|audio|lyrics?|letra|visualizer|" +
        @"hd|hq|4k|1080p|720p|explicit|clean|mv|m/v|remaster(?:ed)?|\d{4}\s+remaster(?:ed)?)\b[^\)\]\}]*[\)\]\}]", O);
    private static readonly Regex NoiseTail = new(@"\s+[-–|]\s+(official\s+)?(music\s+)?(video|audio|lyrics?|visualizer)\s*$", O);
    private static readonly Regex RemasterTail = new(@"\s+[-–]\s+(\d{4}\s+)?remaster(ed)?(\s+\d{4})?(\s+version)?\s*$", O);
    private static readonly Regex Feat = new(@"\s*[\(\[]?\b(?:feat\.?|ft\.?|featuring)\s+([^\)\]]+?)[\)\]]?(?=\s*[\(\[\-–]|$)", O);
    private static readonly Regex VersionGroup = new(@"\s*[\(\[]([^\)\]]*)[\)\]]|\s+[-–]\s+([^\-–]+)$", O);
    private static readonly Regex FeatSplit = new(@",|&| y | and ", O);
    private static readonly Regex Spaces = new(@"\s+", O);
    private static readonly Regex NonWord = new(@"[^\w\s]", O);

    private static readonly (TrackVersion V, Regex R)[] Versions =
    [
        (TrackVersion.Remix, new(@"\b(remix|rmx|mix|bootleg|rework)\b", O)),
        (TrackVersion.Live, new(@"\b(live|en vivo|en directo|unplugged)\b", O)),
        (TrackVersion.Acoustic, new(@"\b(acoustic|acústic[oa])\b", O)),
        (TrackVersion.Instrumental, new(@"\b(instrumental|karaoke)\b", O)),
        (TrackVersion.Cover, new(@"\bcover\b", O)),
        (TrackVersion.Edit, new(@"\b(radio edit|edit|extended)\b", O)),
    ];

    public static CleanTitle Clean(string raw)
    {
        var t = Noise.Replace(raw, "");
        t = NoiseTail.Replace(t, "");
        t = RemasterTail.Replace(t, "");

        var featuring = new List<string>();
        var m = Feat.Match(t);
        if (m.Success)
        {
            featuring = FeatSplit.Split(m.Groups[1].Value)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            t = t.Remove(m.Index, m.Length);
        }

        var version = TrackVersion.Original;
        foreach (Match g in VersionGroup.Matches(t))
        {
            var chunk = g.Groups[1].Success ? g.Groups[1].Value : g.Groups[2].Value;
            var hit = Versions.FirstOrDefault(v => v.R.IsMatch(chunk));
            if (hit.R is null) continue;
            version = hit.V;
            t = t.Replace(g.Value, "");
            break;
        }

        t = Spaces.Replace(t, " ").Trim(' ', '-', '–', '|');
        return new CleanTitle(t, featuring, version);
    }

    public static string DedupeKey(string artist, string title, TrackVersion version = TrackVersion.Original)
    {
        var key = $"{Fold(artist)}|{Fold(title)}|{version.ToString().ToLowerInvariant()}";
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    public static bool DurationsMatch(double a, double b) => Math.Abs(a - b) <= DurationTolerance;

    public static List<T> Deduplicate<T>(IEnumerable<T> items, Func<T, string> key, Func<T, double> duration)
    {
        var seen = new Dictionary<string, List<double>>();
        var result = new List<T>();
        foreach (var item in items)
        {
            var k = key(item);
            var d = duration(item);
            if (!seen.TryGetValue(k, out var list)) seen[k] = list = [];
            if (list.Any(x => DurationsMatch(x, d))) continue;
            list.Add(d);
            result.Add(item);
        }
        return result;
    }

    public static string Fold(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Spaces.Replace(NonWord.Replace(sb.ToString(), " "), " ").Trim();
    }
}
