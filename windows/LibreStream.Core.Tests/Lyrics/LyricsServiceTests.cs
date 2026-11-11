using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class LyricsServiceTests
{
    [Fact]
    public void ParsesLrcWithRepeatsAndOrder()
    {
        var lines = LyricsService.ParseLrc("[00:12.50] Hola\n[ar: Artista]\n[01:02.5][00:05] Coro\nsin marca\n[00:20:30]Fin");
        Assert.Equal(4, lines.Count);
        Assert.Equal(new LyricLine(5, "Coro"), lines[0]);
        Assert.Equal(12.5, lines[1].Time);
        Assert.Equal(20.3, lines[2].Time, 3);
        Assert.Equal(62.5, lines[3].Time);
    }

    [Fact]
    public void IndexAtFollowsPosition()
    {
        var lines = LyricsService.ParseLrc("[00:05] a\n[00:10] b\n[00:15] c");
        Assert.Equal(-1, LyricsService.IndexAt(lines, 2));
        Assert.Equal(0, LyricsService.IndexAt(lines, 5));
        Assert.Equal(1, LyricsService.IndexAt(lines, 14.9));
        Assert.Equal(2, LyricsService.IndexAt(lines, 999));
    }

    [Theory]
    [InlineData("Queen - Bohemian Rhapsody (Official Video Remastered)", "Queen Official", "Queen", "Bohemian Rhapsody")]
    [InlineData("Love of My Life", "Queen Official", "Queen", "Love of My Life")]
    [InlineData("Blinding Lights (Official Audio)", "TheWeekndVEVO", "TheWeeknd", "Blinding Lights")]
    [InlineData("Song", "Artist - Topic", "Artist", "Song")]
    public void GuessesTrackFromVideo(string video, string channel, string artist, string title) =>
        Assert.Equal((artist, title), LyricsService.GuessTrack(video, channel));

    [Fact]
    public void PicksSyncedAndClosestDuration()
    {
        const string json = """
            [{"duration":200,"plainLyrics":"solo texto","syncedLyrics":null},
             {"duration":260,"plainLyrics":"x","syncedLyrics":"[00:01.00] lejos"},
             {"duration":201,"plainLyrics":"x","syncedLyrics":"[00:01.00] cerca"}]
            """;
        var best = LyricsService.Pick(json, 200);
        Assert.NotNull(best);
        Assert.True(best!.Synced);
        Assert.Equal("cerca", best.Lines[0].Text);
    }

    [Fact]
    public void FallsBackToPlainAndHandlesBadJson()
    {
        var plain = LyricsService.Pick("""[{"duration":200,"plainLyrics":"uno\ndos"}]""", 200);
        Assert.False(plain!.Synced);
        Assert.Equal(2, plain.Lines.Count);
        Assert.Null(LyricsService.Pick("[]", 200));
        Assert.Null(LyricsService.Pick("no es json", 200));
    }
}
