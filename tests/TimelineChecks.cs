using GlassPlayer;

internal static class TimelineChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var missing = DateTimeOffset.FromFileTime(0);
        var clock = new MediaTimeline();
        void Observe(string key, double position, double duration, DateTimeOffset updated, bool playing, double elapsed, double? rate = 1, double start = 0, double maxSeek = 0) =>
            clock.Update(key, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration + start), TimeSpan.FromSeconds(position + start), TimeSpan.FromSeconds(maxSeek), updated, playing, rate, now.AddSeconds(elapsed), elapsed);

        Observe("soundcloud", 0, 0, missing, true, 0);
        Check(!clock.HasPosition && clock.PositionAt(100) == 0 && clock.Duration == 0, "unset SoundCloud timeline never adds time elapsed since 1601");
        Check(MediaTimeline.Format(63_000_000_000) == "--:--" && MediaTimeline.Format(double.NaN) == "--:--" && MediaTimeline.Format(double.PositiveInfinity) == "--:--" && MediaTimeline.Format(-1) == "--:--", "invalid times cannot overflow into negative minute counts");
        Check(MediaTimeline.Format(0) == "0:00" && MediaTimeline.Format(125.9) == "2:05" && MediaTimeline.Format(3601) == "60:01", "normal music and long podcast times format correctly");

        Observe("apple", 30, 180, now.AddSeconds(-2), true, 0);
        Check(clock.HasPosition && clock.PositionAt(1) == 33, "valid timestamp interpolates track progress");
        Observe("missing-date", 30, 180, missing, true, 0);
        Observe("missing-date", 30, 180, missing, true, 5);
        Check(clock.PositionAt(6) == 36, "missing update date uses a local clock across repeated polls");
        Observe("missing-date", 30, 180, missing, false, 6);
        Check(clock.PositionAt(20) == 36, "pause freezes the fallback clock");
        Observe("missing-date", 30, 180, missing, true, 20);
        Check(clock.PositionAt(21) == 37, "resume continues without counting paused time");
        Observe("next-track", 0, 120, missing, true, 21);
        Check(clock.PositionAt(22) == 1, "changing tracks resets the previous timeline");
        Observe("next-track", 70, 120, missing, true, 22);
        Check(clock.PositionAt(23) == 71, "new source position replaces fallback after a seek");
        Check(clock.PositionAt(1000) == 120, "interpolation cannot run beyond track duration");

        Observe("future-date", 15, 120, now.AddYears(1), true, 0);
        Check(clock.PositionAt(1) == 16, "future timestamp cannot skew playback time");
        Observe("offset", 20, 180, now, true, 0, start: 100);
        Check(clock.Duration == 180 && clock.PositionAt(1) == 21 && clock.Start == 100, "nonzero timeline start is relative for display and retained for seeking");
        Observe("seek-range", 20, 0, missing, true, 0, maxSeek: 180);
        Check(clock.Duration == 180 && clock.PositionAt(1) == 21, "seek endpoint recovers duration when the track end is missing");
        Observe("no-duration", 15, 0, missing, true, 0);
        Check(clock.HasPosition && clock.Duration == 0 && clock.PositionAt(1) == 16, "known position works without inventing a track duration");
        Observe("invalid-rate", 20, 180, now, true, 0, rate: double.NaN);
        Check(clock.PositionAt(1) == 21 && MediaTimeline.PlaybackRate(double.PositiveInfinity) == 1, "invalid playback rates cannot poison the clock");
        clock.Reset();
        Check(!clock.HasPosition && clock.PositionAt(100) == 0, "disappearing media sessions clear timeline state");
        Console.WriteLine("All 16 timeline checks passed.");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
