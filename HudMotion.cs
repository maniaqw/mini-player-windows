namespace GlassPlayer;

// Timings and easing transcribed from the supplied MusicHud reference.
public static class HudMotion
{
    public const double LyricsLeadSeconds = 0.6;
    public static double BackOut(double progress)
    {
        var p = Math.Clamp(progress, 0, 1) - 1;
        return 1 + 2.9 * p * p * p + 1.9 * p * p;
    }
    public static double Follow(double value, double target, double delta, double speed) => value + (target - value) * (1 - Math.Exp(-Math.Max(0, delta) * Math.Max(0, speed)));
    public static (double Opacity, double X, double Y) Letter(double elapsedMilliseconds, int index, double fontSize, bool incoming)
    {
        var scale = fontSize / 5.5;
        if (incoming)
        {
            var p = Math.Clamp((elapsedMilliseconds - index * 10) / 300, 0, 1);
            return (1 - (1 - p) * (1 - p), -(1 - p) * 1.5 * scale, (1 - BackOut(p)) * 6 * scale);
        }
        var exit = Math.Clamp((elapsedMilliseconds - index * 5) / 200, 0, 1);
        return (1 - exit, exit * exit * scale, -exit * exit * 5 * scale);
    }
    public static (double NewScale, double OldScale, double Opacity) Cover(double elapsedSeconds)
    {
        var p = Math.Clamp(elapsedSeconds / 0.520, 0, 1);
        var eased = 1 - Math.Pow(1 - p, 3);
        return (0.65 + 0.35 * BackOut(eased), 1 - 0.35 * eased, eased);
    }
}
