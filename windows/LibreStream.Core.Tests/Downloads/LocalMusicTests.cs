using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class LocalMusicTests
{
    [Fact]
    public void IdIsStableAndLocal()
    {
        var a = LocalMusic.IdFor(@"C:\Música\Uno.mp3");
        Assert.Equal(a, LocalMusic.IdFor(@"c:\música\uno.MP3"));
        Assert.NotEqual(a, LocalMusic.IdFor(@"C:\Música\Dos.mp3"));
        Assert.True(LocalMusic.IsLocal(a));
        Assert.False(LocalMusic.IsLocal("yt-abc"));
    }

    [Theory]
    [InlineData(@"C:\m\03 - Waka_Waka.mp3", "Waka Waka")]
    [InlineData(@"C:\m\Sin número.flac", "Sin número")]
    [InlineData(@"C:\m\2024.mp3", "2024")]
    public void TitleFromFileNameDropsTrackNumber(string path, string expected) => Assert.Equal(expected, LocalMusic.TitleFromFileName(path));

    [Fact]
    public void LocalItemPlaysFromTheFileWithItsTags()
    {
        var item = LocalMusic.ToCatalogItem(new DownloadedItem("local:abc", "Canción", "Artista", "track", @"C:\m\a.mp3", 100, 200, null));
        Assert.Equal(ItemKind.Track, item.Kind);
        Assert.Equal("Artista", item.Artist);
        var src = Assert.Single(item.Sources);
        Assert.True(src.IsDirectlyPlayable);
        Assert.True(src.Url.IsFile);
    }
}
