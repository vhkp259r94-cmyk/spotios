using LibreStream.Core;
using Xunit;
using V = LibreStream.Core.TrackVersion;

namespace LibreStream.Core.Tests;

public class MetadataMatcherTests
{
    // Mismos casos que tools/test_title_regex.py y MetadataMatcherTests.swift
    public static TheoryData<string, string, string[], V> Cases => new()
    {
        { "Song Name (Official Video)", "Song Name", [], V.Original },
        { "Song Name [Official Music Video] [4K]", "Song Name", [], V.Original },
        { "Song Name (Lyrics)", "Song Name", [], V.Original },
        { "Song Name - Official Audio", "Song Name", [], V.Original },
        { "Song Name (2011 Remaster)", "Song Name", [], V.Original },
        { "Song Name - Remastered 2009", "Song Name", [], V.Original },
        { "Song Name - 2015 Remaster", "Song Name", [], V.Original },
        { "Song Name (feat. Artist B)", "Song Name", ["Artist B"], V.Original },
        { "Song Name ft. Artist B & Artist C", "Song Name", ["Artist B", "Artist C"], V.Original },
        { "Song Name (feat. Artist B) [Official Video]", "Song Name", ["Artist B"], V.Original },
        { "Song Name (Artist X Remix)", "Song Name", [], V.Remix },
        { "Song Name - Club Mix", "Song Name", [], V.Remix },
        { "Song Name (Live at Wembley)", "Song Name", [], V.Live },
        { "Song Name - En Vivo", "Song Name", [], V.Live },
        { "Song Name (Acoustic Version)", "Song Name", [], V.Acoustic },
        { "Song Name (Radio Edit)", "Song Name", [], V.Edit },
        { "Song Name (feat. B) (Live) [HD]", "Song Name", ["B"], V.Live },
        { "Livewire", "Livewire", [], V.Original },
        { "Remix Culture", "Remix Culture", [], V.Original },
        { "Bailando con Lobos", "Bailando con Lobos", [], V.Original },
        { "Señorita (Video Oficial)", "Señorita", [], V.Original },
    };

    [Theory, MemberData(nameof(Cases))]
    public void CleansEdgeCases(string raw, string title, string[] feat, V version) =>
        Assert.Equal(new CleanTitle(title, feat, version), MetadataMatcher.Clean(raw));

    [Fact]
    public void DedupeKeyMatchesPythonAndSwift()
    {
        Assert.Equal("4b498eb814fcbc96514e49ad2f236f1e",
            MetadataMatcher.DedupeKey("Kevin MacLeod", "Monkeys Spinning Monkeys"));
        Assert.Equal(MetadataMatcher.DedupeKey("Beyoncé", "Halo"), MetadataMatcher.DedupeKey("beyonce", "HALO!"));
        Assert.NotEqual(MetadataMatcher.DedupeKey("A", "S", V.Live), MetadataMatcher.DedupeKey("A", "S"));
    }

    [Fact]
    public void DurationTolerance()
    {
        Assert.True(MetadataMatcher.DurationsMatch(200, 204));
        Assert.False(MetadataMatcher.DurationsMatch(200, 204.1));
    }

    [Fact]
    public void Deduplicates() =>
        Assert.Equal(3, MetadataMatcher.Deduplicate(
            new[] { ("a", 200.0), ("a", 203.0), ("a", 260.0), ("b", 200.0) }, x => x.Item1, x => x.Item2).Count);

    [Fact]
    public void ParsesSampleCatalog()
    {
        var items = CatalogService.Parse(File.ReadAllText("catalog.json"));
        Assert.Equal(2, items.Count);
        Assert.Equal(Provider.ArchiveOrg, items[0].Sources[0].Provider);
        Assert.Equal(ItemKind.Video, items[1].Kind);
    }
}
