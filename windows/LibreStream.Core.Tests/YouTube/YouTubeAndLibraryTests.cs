using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class YouTubeAndLibraryTests
{
    private const string Html = """
        <html><script>var ytInitialData = {"contents":{"list":[
          {"videoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"Canción uno"}]},
            "ownerText":{"runs":[{"text":"Canal A"}]},"lengthText":{"simpleText":"3:25"},"publishedTimeText":{"simpleText":"hace 5 d"}}},
          {"shelf":{"items":[{"videoRenderer":{"videoId":"ABC_def-123","title":{"simpleText":"Directo"},
            "longBylineText":{"runs":[{"text":"Canal B"}]}}}]}},
          {"videoRenderer":{"videoId":"abcdefghijk","title":{"runs":[{"text":"Duplicado"}]}}},
          {"videoRenderer":{"videoId":"malo","title":{"runs":[{"text":"Id inválido"}]}}}
        ]}};</script></html>
        """;

    [Fact]
    public void ParsesSearchResults()
    {
        var r = YouTubeSearch.Parse(Html);
        Assert.Equal(2, r.Count);
        Assert.Equal(new YouTubeVideo("abcdefghijk", "Canción uno", "Canal A", 205, "hace 5 d"), r[0]);
        Assert.True(r[1].IsLive);
        Assert.Equal("Canal B", r[1].Channel);
        Assert.Equal("https://i.ytimg.com/vi/abcdefghijk/mqdefault.jpg", r[0].Thumbnail.AbsoluteUri);
    }

    [Theory]
    [InlineData("3:25", 205)]
    [InlineData("1:02:03", 3723)]
    [InlineData("EN DIRECTO", 0)]
    [InlineData(null, 0)]
    public void ParsesLength(string? text, double expected) => Assert.Equal(expected, YouTubeSearch.ParseLength(text));

    [Fact]
    public void NoDataGivesEmptyList() => Assert.Empty(YouTubeSearch.Parse("<html></html>"));

    [Fact]
    public void RejectsUnsafeIds()
    {
        Assert.True(YouTubeSearch.IsValidId("dQw4w9WgXcQ"));
        Assert.False(YouTubeSearch.IsValidId("');alert(1)//"));
        Assert.False(YouTubeSearch.IsValidId(null));
    }

    [Theory]
    [InlineData("https://googleads.g.doubleclick.net/pagead/id", true)]
    [InlineData("https://www.youtube.com/pagead/adview?x=1", true)]
    [InlineData("https://www.youtube.com/api/stats/ads?ver=2", true)]
    [InlineData("https://www.youtube.com/iframe_api", false)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", false)]
    [InlineData("https://rr1---sn-abc.googlevideo.com/videoplayback?id=1", false)]
    [InlineData("https://i.ytimg.com/vi/dQw4w9WgXcQ/mqdefault.jpg", false)]
    [InlineData("https://notdoubleclick.net.example.com/x", false)]
    [InlineData("no es una url", false)]
    public void AdBlockRules(string url, bool blocked) => Assert.Equal(blocked, AdBlock.IsBlocked(url));

    [Fact]
    public void PlaylistsCreateToggleDeleteAndPersist()
    {
        var dir = Path.Combine(Path.GetTempPath(), "librestream-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new SavedItem("youtube", "abcdefghijk", "Canción", "Canal", 205, null);
            var store = new PlaylistStore(dir);
            var list = store.Create("  Favoritas  ");
            Assert.Equal("Favoritas", list.Name);
            Assert.True(store.Toggle(list.Id, item));
            Assert.True(new PlaylistStore(dir).Contains(list.Id, item));
            Assert.False(new PlaylistStore(dir).Toggle(list.Id, item));
            Assert.False(store.Toggle("no-existe", item));
            var reloaded = new PlaylistStore(dir);
            Assert.Single(reloaded.All);
            reloaded.Delete(list.Id);
            Assert.Empty(new PlaylistStore(dir).All);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LibraryTogglesAndPersists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "librestream-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var item = new SavedItem("youtube", "abcdefghijk", "Canción", "Canal", 205, null);
            var store = new LibraryStore(dir);
            Assert.True(store.Toggle(item));
            Assert.True(new LibraryStore(dir).Contains("youtube", "abcdefghijk"));
            Assert.False(new LibraryStore(dir).Toggle(item));
            Assert.Empty(new LibraryStore(dir).Items);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
