using System.Text.Json;
using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class LibraryBackupTests
{
    private static readonly DateTimeOffset When = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExportsTheSameFormatAsTheWebAndPhones()
    {
        var lib = new[]
        {
            new SavedItem("youtube", "BSTsnWoslP4", "Bohemian Rhapsody", "Queen · A Night At The Opera", 355, "https://lh3.googleusercontent.com/abc=w544-h544"),
            new SavedItem("catalog", "cat-1", "Tema libre", "Artista", 120, null),
            new SavedItem("youtube", "../../etc", "Id malo", "x", 1, null),          // identificador raro: se descarta
            new SavedItem("otro", "zz", "Fuente rara", "x", 1, null),               // fuente desconocida: se descarta
        };
        var lists = new[] { new Playlist("p1", "Mi lista", [lib[0]]) };
        using var doc = JsonDocument.Parse(LibraryBackup.Build(lib, lists, When));
        var r = doc.RootElement;
        Assert.Equal(1, r.GetProperty("nsound").GetInt32());
        Assert.Equal("2026-11-12T10:00:00.000Z", r.GetProperty("exportedAt").GetString());
        var items = r.GetProperty("library");
        Assert.Equal(2, items.GetArrayLength());
        var first = items[0];
        Assert.Equal("yt", first.GetProperty("src").GetString());
        Assert.Equal("Queen", first.GetProperty("artist").GetString());
        Assert.Equal("Queen · A Night At The Opera", first.GetProperty("sub").GetString());
        Assert.Equal(355, first.GetProperty("duration").GetDouble());
        Assert.StartsWith("https://", first.GetProperty("thumb").GetString());
        Assert.Equal("cat", items[1].GetProperty("src").GetString());
        Assert.Equal("", items[1].GetProperty("thumb").GetString());
        Assert.Equal("Mi lista", r.GetProperty("playlists")[0].GetProperty("name").GetString());
        Assert.Equal(1, r.GetProperty("playlists")[0].GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void NonHttpsArtworkIsDroppedAndNamesAreClean()
    {
        var lib = new[] { new SavedItem("youtube", "abcdefghijk", "  Doble   espacio  ", "Banda - Topic · Álbum", -5, "http://inseguro/x.jpg") };
        using var doc = JsonDocument.Parse(LibraryBackup.Build(lib, [], When));
        var it = doc.RootElement.GetProperty("library")[0];
        Assert.Equal("Doble espacio", it.GetProperty("title").GetString());
        Assert.Equal("Banda", it.GetProperty("artist").GetString());
        Assert.Equal("", it.GetProperty("thumb").GetString());
        Assert.Equal(0, it.GetProperty("duration").GetDouble());
    }

    [Fact]
    public void FileNameUsesTheDate() =>
        Assert.Equal("nsound-biblioteca-2026-11-12.json", LibraryBackup.FileName(When));
}
