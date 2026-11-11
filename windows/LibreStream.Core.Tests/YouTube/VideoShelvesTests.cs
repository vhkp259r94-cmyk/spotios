using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class VideoShelvesTests
{
    private static YouTubeVideo V(string id, string title, double secs) => new(id.PadRight(11, 'x'), title, "Canal", secs);

    [Fact]
    public void DefaultShelvesAreWellFormed()
    {
        Assert.True(VideoShelves.Default.Length >= 5);
        Assert.All(VideoShelves.Default, s => Assert.NotEmpty(s.Queries));
        Assert.Equal(VideoShelves.Default.Length, VideoShelves.Default.Select(s => s.Title).Distinct().Count());
    }

    [Fact]
    public void MergeAlternatesFiltersAndDeduplicates()
    {
        var a = new List<YouTubeVideo> { V("a1", "Velada combate", 600), V("a2", "Directo", 0), V("a3", "Resumen", 900) };
        var b = new List<YouTubeVideo> { V("b1", "Serie tráiler", 120), V("a1", "Velada combate", 600), V("b2", "Short", 20), V("b3", "Canción (Official Audio)", 200) };
        var merged = VideoShelves.Merge([a, b], 10);
        Assert.Equal(["a1xxxxxxxxx", "b1xxxxxxxxx", "a3xxxxxxxxx"], merged.Select(v => v.Id));   // alterna, sin directos, shorts, audio ni repetidos
        Assert.Single(VideoShelves.Merge([a, b], 1));
        Assert.Empty(VideoShelves.Merge([], 5));
    }
}
