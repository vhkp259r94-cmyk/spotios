using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class ArtistCatalogTests
{
    [Fact]
    public void WeeklyArtistsAreTwelveDistinctAndRotate()
    {
        var a = ArtistCatalog.WeeklyArtists(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        var b = ArtistCatalog.WeeklyArtists(new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(12, a.Count);
        Assert.Equal(12, a.Distinct().Count());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PickArtistNeedsExactNameAndGoodScore()
    {
        const string json = """{"artists":[{"id":"x1","name":"Shakira Fan","score":100},{"id":"s1","name":"Shakira","score":98},{"id":"s2","name":"Shakira","score":60}]}""";
        Assert.Equal("s1", ArtistCatalog.PickArtist(json, "shakira"));
        Assert.Null(ArtistCatalog.PickArtist(json, "Nadie Conocido"));
        Assert.Null(ArtistCatalog.PickArtist("""{"artists":[{"id":"z","name":"Rosalía","score":50}]}""", "Rosalia"));
        Assert.Equal("z", ArtistCatalog.PickArtist("""{"artists":[{"id":"z","name":"Rosalía","score":90}]}""", "Rosalia"));
        Assert.Null(ArtistCatalog.PickArtist("no es json", "x"));
    }

    [Fact]
    public void ReleaseGroupsSkipLiveAndDuplicatesNewestFirst()
    {
        const string json = """
            {"release-groups":[
              {"id":"a","title":"Vieja","primary-type":"Album","first-release-date":"2010-05-01","secondary-types":[]},
              {"id":"b","title":"Nueva","primary-type":"Album","first-release-date":"2024-01-02","secondary-types":[]},
              {"id":"c","title":"En directo","primary-type":"Album","first-release-date":"2020","secondary-types":["Live"]},
              {"id":"d","title":"nueva","primary-type":"EP","first-release-date":"2023","secondary-types":[]}]}
            """;
        var albums = ArtistCatalog.ParseReleaseGroups(json);
        Assert.Equal(["b", "a"], albums.Select(x => x.Id));
        Assert.Equal("2024", albums[0].Year);
        Assert.Equal("https://coverartarchive.org/release-group/b/front-250", albums[0].CoverUrl);
    }
}
