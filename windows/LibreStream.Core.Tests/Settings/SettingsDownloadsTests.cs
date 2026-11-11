using System.Net;
using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class SettingsDownloadsTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "librestream-test-" + Guid.NewGuid().ToString("N"));

    private sealed class FakeHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public void SettingsRoundTripAndNormalize()
    {
        var dir = TempDir();
        try
        {
            var store = new SettingsStore(dir);
            Assert.Equal(AppSettings.BandCount, store.Current.EqBands.Length);
            Assert.True(store.Current.AdBlock);
            store.Current.EqEnabled = true;
            store.Current.EqBands = [30, -30, 1.4, double.NaN];   // fuera de rango, corto y con NaN
            store.Current.Volume = 250;
            store.Current.EqPreset = "No existe";
            store.Save();

            var again = new SettingsStore(dir).Current;
            Assert.True(again.EqEnabled);
            Assert.Equal(10, again.EqBands.Length);
            Assert.Equal([12, -12, 1, 0, 0, 0, 0, 0, 0, 0], again.EqBands);
            Assert.Equal(100, again.Volume);
            Assert.Equal(EqPresets.Custom, again.EqPreset);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CorruptSettingsFallBackToDefaults()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ esto no es json");
            Assert.Equal(80, new SettingsStore(dir).Current.Volume);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void EveryPresetHasTenBandsInRange()
    {
        Assert.Equal(AppSettings.BandCount, EqPresets.BandLabels.Length);
        foreach (var (name, bands) in EqPresets.All)
        {
            Assert.Equal(AppSettings.BandCount, bands.Length);
            Assert.All(bands, b => Assert.InRange(b, -AppSettings.MaxGainDb, AppSettings.MaxGainDb));
            Assert.Equal(name, EqPresets.Match(bands));
        }
        Assert.Equal(EqPresets.Custom, EqPresets.Match(new double[10] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Theory]
    [InlineData("Big Buck Bunny", ".mp4", "Big Buck Bunny.mp4")]
    [InlineData("AC/DC: Back In Black?", "mp4", "AC DC Back In Black.mp4")]
    [InlineData("..\\..\\evil", ".mp4", "evil.mp4")]
    [InlineData("???", ".mp4", "descarga.mp4")]
    public void SafeFileNames(string title, string ext, string expected) =>
        Assert.Equal(expected, FileDownloader.SafeFileName(title, ext));

    [Fact]
    public async Task DownloadsWithProgressAndNoLeftovers()
    {
        var dir = TempDir();
        try
        {
            var body = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
            using var http = new HttpClient(new FakeHandler(body));
            var seen = new List<double>();
            var dest = Path.Combine(dir, "sub", "video.mp4");
            var bytes = await FileDownloader.DownloadAsync(http, new Uri("https://ejemplo.org/v.mp4"), dest, new Progress<double>(seen.Add));
            await Task.Delay(50); // Progress<T> publica en otro hilo
            Assert.Equal(body.Length, bytes);
            Assert.Equal(body, await File.ReadAllBytesAsync(dest));
            Assert.False(File.Exists(dest + ".part"));
            Assert.Equal(1.0, seen.Max());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task FailedOrInsecureDownloadsLeaveNothing()
    {
        var dir = TempDir();
        try
        {
            var dest = Path.Combine(dir, "x.mp4");
            using var bad = new HttpClient(new FakeHandler([1, 2, 3], HttpStatusCode.NotFound));
            await Assert.ThrowsAsync<HttpRequestException>(() => FileDownloader.DownloadAsync(bad, new Uri("https://ejemplo.org/x"), dest));
            await Assert.ThrowsAsync<ArgumentException>(() => FileDownloader.DownloadAsync(bad, new Uri("http://ejemplo.org/x"), dest));
            Assert.False(File.Exists(dest));
            Assert.False(File.Exists(dest + ".part"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DownloadStoreTracksAndDeletes()
    {
        var dir = TempDir();
        try
        {
            var file = Path.Combine(dir, "a.mp4");
            Directory.CreateDirectory(dir);
            File.WriteAllText(file, "contenido");
            var item = new DownloadedItem("id1", "Título", "Artista", "video", file, 9, 60, null);
            var store = new DownloadStore(dir);
            store.Add(item);
            Assert.Equal(item, new DownloadStore(dir).Find("id1"));

            File.Delete(file);                               // borrado a mano: se olvida al reabrir
            Assert.Empty(new DownloadStore(dir).Items);

            File.WriteAllText(file, "otra vez");
            var again = new DownloadStore(dir);
            again.Add(item);
            again.Remove("id1");
            Assert.False(File.Exists(file));
            Assert.Empty(new DownloadStore(dir).Items);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("Queen - Love of My Life", 217, true, true)]
    [InlineData("Queen - Love of My Life (Official Audio)", 217, true, false)]
    [InlineData("Queen - Love of My Life (Lyrics)", 217, true, false)]
    [InlineData("Piano tutorial: Love of My Life", 300, false, true)]
    [InlineData("Best rock mix 2026", 300, false, true)]
    [InlineData("Remix Culture - DJ Snake", 200, true, true)]
    [InlineData("Rock hits - 2 hours", 7200, false, true)]
    [InlineData("Canción larga", 900, false, true)]
    [InlineData("Directo permanente", 0, false, false)]
    public void ContentFilterSeparatesMusicAndVideo(string title, double seconds, bool song, bool video) =>
        Assert.Equal((song, video), (ContentFilter.IsSong(new YouTubeVideo("abcdefghijk", title, "Canal", seconds)),
                                     ContentFilter.IsVideo(new YouTubeVideo("abcdefghijk", title, "Canal", seconds))));
}
