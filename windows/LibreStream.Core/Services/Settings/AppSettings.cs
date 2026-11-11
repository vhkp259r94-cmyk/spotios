using System.Text.Json;

namespace LibreStream.Core;

/// <summary>Ajustes de la app, guardados en settings.json.</summary>
public sealed class AppSettings
{
    public const int BandCount = 10;
    public const double MaxGainDb = 12;

    public bool EqEnabled { get; set; }
    public string EqPreset { get; set; } = "Plano";
    public double[] EqBands { get; set; } = new double[BandCount];
    public string? DownloadFolder { get; set; }
    public bool AdBlock { get; set; } = true;
    public double Volume { get; set; } = 80;
    public string Look { get; set; } = Themes.Default;

    /// <summary>Corrige valores fuera de rango o con forma inesperada (archivo editado a mano o antiguo).</summary>
    public void Normalize()
    {
        var bands = new double[BandCount];
        for (var i = 0; i < BandCount && EqBands is not null && i < EqBands.Length; i++)
            bands[i] = double.IsFinite(EqBands[i]) ? Math.Clamp(Math.Round(EqBands[i]), -MaxGainDb, MaxGainDb) : 0;
        EqBands = bands;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 100) : 80;
        if (string.IsNullOrWhiteSpace(EqPreset) || (EqPreset != EqPresets.Custom && !EqPresets.All.ContainsKey(EqPreset)))
            EqPreset = EqPresets.Custom;
        if (string.IsNullOrWhiteSpace(DownloadFolder)) DownloadFolder = null;
        if (Themes.All.All(t => t.Id != Look)) Look = Themes.Default;
    }
}

/// <summary>Ecualizador de 10 bandas (dB) y sus ajustes predefinidos.</summary>
public static class EqPresets
{
    public const string Custom = "Personalizado";

    public static readonly string[] BandLabels = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    public static readonly IReadOnlyDictionary<string, double[]> All = new Dictionary<string, double[]>
    {
        ["Plano"] = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ["Graves"] = [6, 5, 4, 2, 0, 0, 0, 0, 0, 0],
        ["Agudos"] = [0, 0, 0, 0, 0, 1, 3, 5, 6, 7],
        ["Voz"] = [-2, -2, -1, 1, 3, 4, 3, 1, 0, -1],
        ["Rock"] = [5, 4, 2, -1, -2, -1, 2, 4, 5, 6],
        ["Pop"] = [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2],
        ["Hip hop"] = [6, 5, 2, 3, -1, -1, 2, 0, 2, 3],
        ["Electrónica"] = [6, 5, 2, 0, -2, 2, 1, 2, 5, 6],
        ["Clásica"] = [4, 3, 2, 1, -1, -1, 0, 2, 3, 4],
    };

    /// <summary>Nombre del ajuste que coincide con las bandas, o <see cref="Custom"/>.</summary>
    public static string Match(IReadOnlyList<double> bands) =>
        All.FirstOrDefault(p => p.Value.SequenceEqual(bands)).Key ?? Custom;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _file;

    public AppSettings Current { get; private set; }

    public SettingsStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "settings.json");
        try
        {
            Current = File.Exists(_file)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            Current = new AppSettings();
        }
        Current.Normalize();
    }

    public void Save()
    {
        Current.Normalize();
        File.WriteAllText(_file, JsonSerializer.Serialize(Current, Json));
    }

    /// <summary>Carpeta de descargas por defecto: Vídeos\LibreStream (o la de datos si no existe).</summary>
    public static string DefaultDownloadFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LibreStream", "Descargas")
            : Path.Combine(videos, "LibreStream");
    }

    public string DownloadFolder => Current.DownloadFolder ?? DefaultDownloadFolder();
}
