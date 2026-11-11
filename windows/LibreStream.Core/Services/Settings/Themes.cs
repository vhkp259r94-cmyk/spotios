namespace LibreStream.Core;

/// <summary>Paleta de un estilo de la interfaz. Los mismos estilos existen en la interfaz web (iOS/Android) con estos colores.</summary>
public sealed record ThemePalette(string Id, string Name, bool Dark, string Bg, string Surface, string Accent, string Accent2, string Blue, string On);

public static class Themes
{
    public const string Default = "nsound";

    public static readonly ThemePalette[] All =
    [
        new("nsound", "Nsound", true, "#0B0C10", "#14161D", "#FF4D62", "#D6233F", "#3B82F6", "#FFFFFF"),
        new("neon", "Neón", true, "#07060D", "#110D20", "#FF2BD6", "#A21CAF", "#22D3EE", "#FFFFFF"),
        new("oceano", "Océano", true, "#05111B", "#0C1B2A", "#2DD4BF", "#0891B2", "#38BDF8", "#042F2E"),
        new("atardecer", "Atardecer", true, "#130B0D", "#1F1214", "#FF8A3D", "#E11D74", "#A855F7", "#2A1000"),
        new("amoled", "AMOLED", true, "#000000", "#0B0B0D", "#FF4D62", "#D6233F", "#3B82F6", "#FFFFFF"),
        new("claro", "Claro", false, "#F4F5F8", "#FFFFFF", "#E11D48", "#BE123C", "#2563EB", "#FFFFFF"),
        new("clasico", "Clásico", true, "#121214", "#101014", "#FF6B6B", "#E5465C", "#3B82F6", "#1A0505"),
    ];

    /// <summary>El estilo con ese id, o el predeterminado si no existe.</summary>
    public static ThemePalette Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
