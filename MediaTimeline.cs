using System.Globalization;

namespace GlassPlayer;

public sealed class MediaTimeline
{
    public const double MaximumSeconds = 7 * 24 * 60 * 60;
    private string? lastKey;
    private (double Start, double End, double Position, DateTimeOffset Updated) lastSample;
    private double anchor, observedAt, rate = 1;
    private bool playing;
    public double Duration { get; private set; }
    public double Start { get; private set; }
    public bool HasPosition { get; private set; }
    public double Rate => rate;
    public static bool ValidDuration(double value) => double.IsFinite(value) && value > 0 && value <= MaximumSeconds;
    public static double ClampPosition(double position, double duration) => double.IsFinite(position) ? Math.Clamp(position, 0, ValidDuration(duration) ? duration : MaximumSeconds) : 0;
    public static double PlaybackRate(double? value) => value is > 0 and <= 16 && double.IsFinite(value.Value) ? value.Value : 1;
    public double PositionAt(double seconds) => HasPosition ? ClampPosition(anchor + (playing ? Math.Max(0, seconds - observedAt) * rate : 0), Duration) : 0;

    public void Update(string key, TimeSpan start, TimeSpan end, TimeSpan position, TimeSpan maxSeek, DateTimeOffset updated, bool isPlaying, double? playbackRate, DateTimeOffset now, double seconds)
    {
        double previous = PositionAt(seconds);
        bool continuing = key == lastKey && HasPosition;
        var sample = (start.TotalSeconds, end.TotalSeconds, position.TotalSeconds, updated);
        bool samePosition = continuing && sample.Item1 == lastSample.Start && sample.Item2 == lastSample.End && sample.Item3 == lastSample.Position;
        double span = end.TotalSeconds - start.TotalSeconds;
        if (!ValidDuration(span)) span = maxSeek.TotalSeconds - start.TotalSeconds;
        Duration = ValidDuration(span) ? span : 0;
        Start = Math.Abs(start.TotalSeconds) <= MaximumSeconds ? start.TotalSeconds : 0;
        double raw = position.TotalSeconds - start.TotalSeconds;
        double age = (now - updated).TotalSeconds;
        // Unset WinRT dates (1601), stale dates and future dates cannot anchor a playback clock.
        bool usableDate = age >= 0 && age <= (Duration > 0 ? Duration : 86400);
        bool usablePosition = double.IsFinite(raw) && raw >= 0 && raw <= MaximumSeconds;
        HasPosition = usablePosition && (Duration > 0 || raw > 0 || usableDate);
        double nextRate = PlaybackRate(playbackRate);
        double candidate = raw + (isPlaying && usableDate ? age * nextRate : 0);
        // SMTC positions are often quantized or republished with a newer date.
        // Keep a local clock for repeated positions, and slew small corrections
        // instead of moving backwards across a lyric boundary on each poll.
        if (samePosition) candidate = previous;
        else if (continuing && (playing || isPlaying) && Math.Abs(candidate - previous) <= 2)
        {
            if (playing && isPlaying)
                nextRate *= 1 + Math.Clamp((candidate - previous) / 2, -0.2, 0.2);
            candidate = previous;
        }
        rate = nextRate;
        anchor = HasPosition ? ClampPosition(candidate, Duration) : 0;
        playing = isPlaying;
        observedAt = seconds;
        lastKey = key;
        lastSample = sample;
    }
    public void Seek(double position, double seconds)
    {
        anchor = ClampPosition(position, Duration);
        observedAt = seconds;
    }
    public void Reset() { lastKey = null; anchor = Duration = 0; HasPosition = playing = false; }
    public static string Format(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > MaximumSeconds) return "--:--";
        long whole = (long)seconds;
        return (whole / 60).ToString(CultureInfo.InvariantCulture) + ":" + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}
