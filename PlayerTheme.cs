using System.Windows.Media;

namespace GlassPlayer;

public sealed record PlayerTheme(string Name, string BackgroundHex, string AccentHex, string TextHex, string SecondaryHex, string LightHex)
{
    public Color BackgroundColor => (Color)ColorConverter.ConvertFromString(BackgroundHex);
    public Color LightColor => (Color)ColorConverter.ConvertFromString(LightHex);
    public SolidColorBrush Background => Brush(BackgroundHex);
    public SolidColorBrush Accent => Brush(AccentHex);
    public SolidColorBrush Text => Brush(TextHex);
    public SolidColorBrush Secondary => Brush(SecondaryHex);
    public IReadOnlyDictionary<string, SolidColorBrush> SettingsBrushes()
    {
        var background = BackgroundColor;
        var text = ((SolidColorBrush)Text).Color;
        var accent = Accent.Color;
        SolidColorBrush Mix(Color from, Color to, double amount)
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * amount), (byte)Math.Round(from.G + (to.G - from.G) * amount), (byte)Math.Round(from.B + (to.B - from.B) * amount)));
            brush.Freeze();
            return brush;
        }
        return new Dictionary<string, SolidColorBrush>
        {
            ["AccentBrush"] = Accent,
            ["SettingsBackground"] = Background,
            ["SettingsText"] = Text,
            ["SettingsSecondary"] = Secondary,
            ["SettingsMuted"] = Mix(background, text, 0.57),
            ["SettingsSurface"] = Mix(background, text, 0.035),
            ["SettingsCard"] = Mix(background, text, 0.045),
            ["SettingsHover"] = Mix(background, text, 0.085),
            ["SettingsSelected"] = Mix(background, accent, 0.13),
            ["SettingsBorder"] = Mix(background, text, 0.17),
            ["SettingsHoverBorder"] = Mix(background, accent, 0.6),
            ["SettingsTrack"] = Mix(background, text, 0.20),
            ["SettingsThumb"] = Text,
        };
    }
    public static SolidColorBrush Brush(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    public static IReadOnlyList<PlayerTheme> All { get; } = new PlayerTheme[]
    {
        new("Zenith", "#101116", "#A5AEFF", "#F1F1F8", "#ACADB9", "#CFD3FF"),
        new("Amethyst", "#180F1D", "#CF8EEF", "#F6ECFC", "#C4ACCD", "#ECD1FF"),
        new("Synthwave", "#210F1C", "#FA72CB", "#FFEAF8", "#D5AEC9", "#FFCEE9"),
        new("Rosewood", "#211315", "#F28C99", "#FFEFF0", "#CEB0B3", "#FFD3DA"),
        new("Ember", "#23170F", "#FF973E", "#FFF2E6", "#CDB9A7", "#FFE0AE"),
        new("Goldleaf", "#201E10", "#D7BF35", "#FBF8DE", "#C2BEA0", "#FFF0B0"),
        new("Toxic", "#151E0D", "#91D800", "#F0F9E5", "#ABB89E", "#DFFF9C"),
        new("Verdant", "#0F201B", "#30D692", "#E4F8EF", "#9FBEAF", "#AEFFE0"),
        new("Lagoon", "#142F36", "#31CFC4", "#F1F6F3", "#D4E1DB", "#C9F5EF"),
        new("Glacier", "#101D28", "#49B8F4", "#EDF6FD", "#A5BCCC", "#C8ECFF"),
        new("Graphite", "#17181B", "#ACADB8", "#F1F1F3", "#AEB0B8", "#E4E6EF"),
        new("Porcelain", "#F0F3F8", "#5576AA", "#273348", "#637084", "#FFFFFF"),
    };
    public static PlayerTheme Find(string? name) => All.FirstOrDefault(t => t.Name == name) ?? All.First(t => t.Name == "Lagoon");
}
