using System.Text.Json;
using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class LinkImportTests
{
    [Fact]
    public void RecognizesTheFiveServices()
    {
        var (sp, _) = LinkImport.ParseLink("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M?si=x");
        Assert.Equal(("spotify", "playlist"), (sp!.Service, sp.Kind));
        Assert.Equal("https://open.spotify.com/embed/artist/06HL4z0CvFAxyc27GXpf02", LinkImport.ParseLink("https://open.spotify.com/intl-es/artist/06HL4z0CvFAxyc27GXpf02").Link!.FetchUrl);
        Assert.Equal("https://api.deezer.com/playlist/1313621735", LinkImport.ParseLink("https://www.deezer.com/es/playlist/1313621735").Link!.FetchUrl);
        Assert.Contains("/artist/27/top", LinkImport.ParseLink("https://www.deezer.com/es/artist/27").Link!.FetchUrl);
        Assert.Equal("track", LinkImport.ParseLink("https://youtu.be/dQw4w9WgXcQ").Link!.Kind);
        Assert.Equal("playlist", LinkImport.ParseLink("https://www.youtube.com/playlist?list=PLabcdef123456").Link!.Kind);
        Assert.Equal("playlist", LinkImport.ParseLink("https://music.apple.com/es/playlist/x/pl.abc").Link!.Kind);
        Assert.Equal("track", LinkImport.ParseLink("https://soundcloud.com/forss/flickermood").Link!.Kind);
    }

    [Theory]
    [InlineData("https://open.spotify.com/album/abc1234567890", "álbumes")]
    [InlineData("https://www.youtube.com/@canal", "canales")]
    [InlineData("https://music.apple.com/es/album/x/123", "álbumes")]
    [InlineData("https://soundcloud.com/forss/sets/s", "SoundCloud")]
    [InlineData("hola", "enlace")]
    [InlineData("https://example.com/x", "no compatible")]
    [InlineData("javascript:alert(1)", "enlace")]
    public void RejectsWhatItCannotImport(string text, string part)
    {
        var (link, error) = LinkImport.ParseLink(text);
        Assert.Null(link);
        Assert.Contains(part, error);
    }

    [Fact]
    public void ReadsSpotifyDeezerYoutubeAndSoundcloud()
    {
        var spBody = "<script id=\"__NEXT_DATA__\" type=\"application/json\">" + JsonSerializer.Serialize(new
        {
            props = new { pageProps = new { state = new { data = new { entity = new { name = "Mi lista", trackList = new[] { new { title = "A", subtitle = "X" }, new { title = "A", subtitle = "X" }, new { title = "B", subtitle = "Y" } } } } } } },
        }) + "</script>";
        var sp = LinkImport.Parse(new ImportLink("spotify", "playlist", "x", ""), spBody);
        Assert.Equal("Mi lista", sp.Name);
        Assert.Equal([new ImportTrack("A", "X"), new ImportTrack("B", "Y")], sp.Tracks);

        var dz = LinkImport.Parse(new ImportLink("deezer", "playlist", "1", ""), """{"title":"L","tracks":{"data":[{"title":"T","artist":{"name":"Z"}}]}}""");
        Assert.Equal([new ImportTrack("T", "Z")], dz.Tracks);

        var yt = LinkImport.Parse(new ImportLink("youtube", "playlist", "x", ""),
            "var ytInitialData = {\"a\":{\"playlistHeaderRenderer\":{\"title\":{\"simpleText\":\"PL\"}},\"b\":[{\"playlistVideoRenderer\":{\"title\":{\"runs\":[{\"text\":\"Song\"}]},\"shortBylineText\":{\"runs\":[{\"text\":\"Band - Topic\"}]}}}]}};</script>");
        Assert.Equal("PL", yt.Name);
        Assert.Equal([new ImportTrack("Song", "Band")], yt.Tracks);

        var sc = LinkImport.Parse(new ImportLink("soundcloud", "track", "x", ""), """{"title":"Flickermood by Forss","author_name":"Forss"}""");
        Assert.Equal([new ImportTrack("Flickermood", "Forss")], sc.Tracks);

        Assert.Empty(LinkImport.Parse(new ImportLink("spotify", "track", "x", ""), "basura").Tracks);
    }

    [Fact]
    public void CapsTheListAt500()
    {
        var data = string.Join(",", Enumerable.Range(0, 700).Select(i => $"{{\"title\":\"t{i}\",\"artist\":{{\"name\":\"a\"}}}}"));
        var info = LinkImport.Parse(new ImportLink("deezer", "playlist", "1", ""), $"{{\"title\":\"L\",\"tracks\":{{\"data\":[{data}]}}}}");
        Assert.Equal(LinkImport.Max, info.Tracks.Count);
        Assert.Equal(500, LinkImport.Max);
    }

    [Fact]
    public void PicksTheRightSong()
    {
        var res = new[]
        {
            new YouTubeVideo("aaaaaaaaaaa", "Loser (Live)", "Otro grupo · Directo", 200),
            new YouTubeVideo("bbbbbbbbbbb", "Loser", "Tame Impala · Currents", 223),
        };
        Assert.Equal("bbbbbbbbbbb", LinkImport.PickMatch(new ImportTrack("Loser", "Tame Impala"), res)!.Id);
        Assert.Null(LinkImport.PickMatch(new ImportTrack("Inexistente", "Nadie"), res));
    }

    [Fact]
    public async Task RefusesHostsOutsideTheList()
    {
        using var http = new HttpClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => LinkImport.FetchAsync(http, "https://example.com/"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => LinkImport.FetchAsync(http, "http://www.youtube.com/"));
    }

    [Fact]
    public void PlaylistAddManyDoesNotRepeat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nsound-test-" + Guid.NewGuid().ToString("N"));
        var store = new PlaylistStore(dir);
        var list = store.Create("X");
        SavedItem Mk(int n) => new("youtube", "id" + n, "t" + n, "a", 100, null);
        Assert.Equal(2, store.AddMany(list.Id, [Mk(1), Mk(2), Mk(2)]));
        Assert.Equal(1, store.AddMany(list.Id, [Mk(2), Mk(3)]));
        Assert.Equal(3, store.Find(list.Id)!.Items.Count);
        Assert.Equal(0, store.AddMany("nope", [Mk(9)]));
        Directory.Delete(dir, true);
    }
}
