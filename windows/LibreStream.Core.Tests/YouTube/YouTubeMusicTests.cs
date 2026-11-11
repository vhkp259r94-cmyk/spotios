using System.Text.Json;
using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class YouTubeMusicTests
{
    private static string Song(string id, string title, string[] parts, string art)
    {
        var runs = string.Join(",", parts.SelectMany((p, i) => i == 0
            ? new[] { "{\"text\":" + JsonSerializer.Serialize(p) + "}" }
            : new[] { "{\"text\":\" • \"}", "{\"text\":" + JsonSerializer.Serialize(p) + "}" }));
        return """
            {"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"@ID"},
              "thumbnail":{"musicThumbnailRenderer":{"thumbnail":{"thumbnails":[{"url":"https://lh3.googleusercontent.com/x=w60-h60"},{"url":"@ART"}]}}},
              "flexColumns":[
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":@TITLE}]}}},
                {"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[@RUNS]}}}]}}
            """.Replace("@ID", id).Replace("@ART", art).Replace("@TITLE", JsonSerializer.Serialize(title)).Replace("@RUNS", runs);
    }

    [Fact]
    public void ParsesSongsWithArtistAlbumDurationAndArt()
    {
        var json = "{\"contents\":{\"tabs\":[{\"shelf\":{\"contents\":["
            + Song("BSTsnWoslP4", "Bohemian Rhapsody", ["Queen", "A Night At The Opera", "5:55"], "https://lh3.googleusercontent.com/abc=w226-h226-l90-rj") + ","
            + Song("2iyV1rsnCzQ", "Single suelto", ["Artista", "3:30"], "") + ","
            + Song("malo", "Id inválido", ["X", "1:00"], "") + ","
            + Song("BSTsnWoslP4", "Repetida", ["Queen", "5:55"], "")
            + "]}}]}}";
        var songs = YouTubeMusic.Parse(json);
        Assert.Equal(2, songs.Count);
        Assert.Equal(new YouTubeVideo("BSTsnWoslP4", "Bohemian Rhapsody", "Queen · A Night At The Opera", 355, null,
            "https://lh3.googleusercontent.com/abc=w544-h544"), songs[0]);
        Assert.Equal("Artista", songs[1].Channel);
        Assert.Equal(210, songs[1].DurationSec);
        Assert.Equal("https://lh3.googleusercontent.com/abc=w544-h544", songs[0].Thumbnail.AbsoluteUri);
        Assert.StartsWith("https://i.ytimg.com/vi/2iyV1rsnCzQ/", songs[1].Thumbnail.AbsoluteUri);   // sin carátula: miniatura de YouTube
    }

    [Fact]
    public void BadOrEmptyResponsesGiveNoSongs()
    {
        Assert.Empty(YouTubeMusic.Parse("{ roto"));
        Assert.Empty(YouTubeMusic.Parse("{}"));
    }

    [Fact]
    public void BodyAndGenreQueries()
    {
        using var doc = JsonDocument.Parse(YouTubeMusic.SearchBody("  queen "));
        Assert.Equal("queen", doc.RootElement.GetProperty("query").GetString());
        Assert.Equal("WEB_REMIX", doc.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientName").GetString());
        Assert.Equal("éxitos 2026", YouTubeMusic.GenreQuery("Éxitos", 2026));
        Assert.Equal("lofi", YouTubeMusic.GenreQuery("Lo-fi", 2026));
        Assert.Equal("Otro", YouTubeMusic.GenreQuery("Otro", 2026));
    }
}
