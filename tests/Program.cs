using System.Reflection;
using System.Windows;
using System.Windows.Media;
using GlassPlayer;
using Localization = GlassPlayer.Localization;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var lines = LyricsService.Parse("[ti:Metadata]\n[offset:+500]\n[00:12.345][00:30.1]Repeated\n[00:02.00]First\n[00:04.00]\n[00:99.00]Invalid\nnot lyrics");
        Check(lines.Count == 4, "metadata and malformed timestamps excluded; repeated timestamps expanded");
        Check(lines[0].Text == "First" && Math.Abs(lines[0].Seconds - 1.5) < 0.001, "sorted timestamps and positive LRC offset");
        Check(lines[1].Text == "" && Math.Abs(lines[1].Seconds - 3.5) < 0.001, "instrumental gaps preserved");
        Check(Math.Abs(lines[2].Seconds - 11.845) < 0.001 && Math.Abs(lines[3].Seconds - 29.6) < 0.001, "fractional timestamps");
        Check(LyricsService.Parse("[offset: -1000]\n[00:01:50]Line")[0].Seconds == 2.5, "negative offset and colon fraction");
        Check(LyricsService.Parse("[offset:1000]\n[offset:2000]\n[00:01.00]Line")[0].Seconds == 0, "last offset wins; negative times clamped");

        var app = new System.Windows.Application();
        var window = new MainWindow(true);
        var ring = (System.Windows.Shapes.Path)window.FindName("ProgressRing");
        var draw = typeof(MainWindow).GetMethod("DrawRing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        draw.Invoke(window, [0.0]);
        Check(ring.Data is null, "empty progress at zero");
        draw.Invoke(window, [0.25]);
        var quarter = PathGeometry.CreateFromGeometry(ring.Data);
        var quarterArc = (ArcSegment)quarter.Figures[0].Segments[0];
        Check(quarterArc.Point == new Point(54, 28.5) && !quarterArc.IsLargeArc && quarterArc.SweepDirection == SweepDirection.Clockwise, "quarter progress points right, clockwise from noon");
        draw.Invoke(window, [0.75]);
        var threeQuarter = PathGeometry.CreateFromGeometry(ring.Data);
        Check(((ArcSegment)threeQuarter.Figures[0].Segments[0]).IsLargeArc, "progress over halfway follows long arc");
        draw.Invoke(window, [1.0]);
        Check(ring.Data is EllipseGeometry, "full progress is closed circle");
        Check(PlayerTheme.All.Count == 12 && PlayerTheme.All.Select(t => t.Name).Distinct().Count() == 12, "twelve distinct theme presets");
        Check(PlayerTheme.Find("unknown").Name == "Lagoon", "unknown theme falls back to Lagoon");
        window.SetAppearance("Porcelain", 1.25);
        Check(window.CurrentTheme.Name == "Porcelain" && window.Height == 180, "light theme and whole-player scale applied");
        window.SetAppearance("Amethyst", double.NaN);
        Check(window.PlayerScale == 1 && window.Height == 144, "invalid scale falls back to 100 percent");
        window.SetAppearance("Lagoon", 100);
        Check(window.PlayerScale == 1.5 && window.Height == 216, "scale is limited to 150 percent");
        window.SetAppearance("Lagoon", 1);
        window.SetLanguage("en");
        Check(Localization.T("Настройки плеера") == "Player settings", "English menu translation");
        Check((string)app.Resources["UiAppearance"] == "Appearance", "English XAML resources updated live");
        Check(Localization.T("An actual song title") == "An actual song title", "music metadata is preserved");
        window.SetLanguage("ru");
        Check(Localization.T("Настройки плеера") == "Настройки плеера" && (string)app.Resources["UiAppearance"] == "Внешний вид", "Russian resources restored live");
        bool allThemeBackgroundsMatch = true, allThemeTextReadable = true;
        double Luminance(Color color)
        {
            double Linear(byte component) { var value = component / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        foreach (var theme in PlayerTheme.All)
        {
            var brushes = theme.SettingsBrushes();
            allThemeBackgroundsMatch &= brushes["SettingsBackground"].Color == theme.BackgroundColor;
            double background = Luminance(brushes["SettingsBackground"].Color);
            foreach (var key in new[] { "SettingsText", "SettingsSecondary", "SettingsMuted" })
            {
                double foreground = Luminance(brushes[key].Color);
                allThemeTextReadable &= (Math.Max(background, foreground) + 0.05) / (Math.Min(background, foreground) + 0.05) >= 3;
            }
        }
        Check(allThemeBackgroundsMatch, "settings background matches player palette in all twelve themes");
        Check(allThemeTextReadable, "settings text keeps contrast in dark and light themes");
        Check(HudMotion.Letter(100, 20, 23, true).Opacity == 0 && HudMotion.Letter(100, 0, 23, true).Opacity > 0, "letters enter individually with ten millisecond stagger");
        var letterEnd = HudMotion.Letter(600, 30, 23, true);
        Check(letterEnd.Opacity == 1 && Math.Abs(letterEnd.Y) < 0.001, "incoming letters settle after 300 milliseconds each");
        var outgoing = HudMotion.Letter(100, 0, 23, false);
        Check(outgoing.Y < 0 && outgoing.Opacity is > 0 and < 1, "outgoing letters move upward and fade");
        Check(HudMotion.BackOut(0.7) > 1 && HudMotion.BackOut(1) == 1, "reference back easing overshoots then settles");
        var coverStart = HudMotion.Cover(0);
        var coverEnd = HudMotion.Cover(0.520);
        Check(coverStart.NewScale == 0.65 && coverStart.Opacity == 0 && coverEnd.NewScale == 1 && coverEnd.Opacity == 1, "cover change starts at 65 percent and completes in 520 milliseconds");
        double spin = 0;
        for (int i = 0; i < 120; i++) spin = HudMotion.Follow(spin, 55, 1.0 / 60, 2.2);
        Check(spin > 54 && spin < 55 && HudMotion.Follow(spin, 0, 1.0 / 60, 3.5) < spin, "vinyl speed accelerates toward 55 degrees and coasts down on pause");
        DesignChecks.Run(window);
        app.Shutdown();
        Console.WriteLine("All 27 checks passed.");
        TimelineChecks.Run();
        Task.Run(DiscordChecks.RunAsync).GetAwaiter().GetResult();
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }
}
