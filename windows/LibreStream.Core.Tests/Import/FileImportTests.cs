using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class FileImportTests
{
    [Fact]
    public void ReadsExportifyCsvWithQuotesAndFirstArtist()
    {
        const string csv = "Track URI,Track Name,Artist Name(s),Album Name\n" +
            "spotify:track:1,\"Hola, mundo\",Ana;Luis,Disco\n" +
            "spotify:track:2,Segunda,Pepe,Disco\n" +
            "spotify:track:3,Segunda,Pepe,Otro\n";
        var (lists, error) = FileImport.Parse("Mi lista.csv", csv);
        Assert.Null(error);
        var l = Assert.Single(lists!);
        Assert.Equal("Mi lista", l.Name);
        Assert.Equal([new ImportTrack("Hola, mundo", "Ana"), new ImportTrack("Segunda", "Pepe")], l.Tracks);   // sin repetidas
    }

    [Fact]
    public void ReadsSpotifyDataExportJsonWithSeveralPlaylists()
    {
        const string json = """
            {"playlists":[
              {"name":"Fiesta","items":[{"track":{"trackName":"Uno","artistName":"A"}},{"track":null},{"track":{"trackName":"Dos","artistName":"B"}}]},
              {"name":"Vacía","items":[]}]}
            """;
        var (lists, error) = FileImport.Parse("Playlist1.json", json);
        Assert.Null(error);
        var l = Assert.Single(lists!);
        Assert.Equal("Fiesta", l.Name);
        Assert.Equal(2, l.Tracks.Count);
    }

    [Fact]
    public void ReadsGenericJsonAndPlainTextLines()
    {
        var (a, _) = FileImport.Parse("x.json", """{"name":"Mix","tracks":[{"title":"T1","artists":[{"name":"Art"}]},{"name":"T2","artist":"Otro"}]}""");
        Assert.Equal("Mix", a![0].Name);
        Assert.Equal([new ImportTrack("T1", "Art"), new ImportTrack("T2", "Otro")], a[0].Tracks);

        var (b, _) = FileImport.Parse("lista.txt", "Shakira - Waka Waka\nBad Bunny – Tití Me Preguntó\n\n");
        Assert.Equal([new ImportTrack("Waka Waka", "Shakira"), new ImportTrack("Tití Me Preguntó", "Bad Bunny")], b![0].Tracks);
    }

    [Fact]
    public void ExplainsWhatIsWrong()
    {
        Assert.Equal("El archivo JSON no se puede leer.", FileImport.Parse("a.json", "{roto").Error);
        Assert.StartsWith("No reconozco ese JSON", FileImport.Parse("a.json", """{"otra":1}""").Error);
        Assert.StartsWith("No reconozco el formato", FileImport.Parse("a.txt", "una línea\notra línea").Error);
        Assert.Equal("No encontré canciones en ese archivo.", FileImport.Parse("a.csv", "Track Name,Artist\n,\n").Error);
    }
}
