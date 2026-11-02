using GlassPlayer;

internal static class MotionChecks
{
    public static void Run()
    {
        foreach (double hz in new[] { 59.94, 60, 120, 144 })
        {
            var clock = new RenderFrameClock();
            int frames = 0;
            for (int i = 0; i < 240; i++)
            {
                var time = TimeSpan.FromSeconds(i / hz);
                if (clock.Advance(time, out var delta)) { frames++; if (delta <= 0 || delta > 0.1) throw new Exception("Invalid frame delta"); }
                CheckDuplicate(clock, time);
            }
            Check(frames == 240, $"all unique frames are animated at {hz} Hz without a second 60 FPS limiter");
        }
        var jitter = new RenderFrameClock();
        bool allJitteredFramesAccepted = true;
        for (int i = 0; i < 120; i++)
            allJitteredFramesAccepted &= jitter.Advance(TimeSpan.FromSeconds(i / 60.0 + (i % 2 == 0 ? 0.0004 : -0.0004)), out _);
        Check(allJitteredFramesAccepted, "sub-millisecond callback jitter cannot discard alternate frames");
        var resumed = new RenderFrameClock();
        resumed.Advance(TimeSpan.Zero, out _);
        resumed.Advance(TimeSpan.FromSeconds(30), out var pause);
        Check(pause == 0.1, "resuming after a stall limits motion catch-up");
        resumed.Reset();
        Check(resumed.Advance(TimeSpan.Zero, out var first) && Math.Abs(first - 1.0 / 60) < 1e-10, "restoring the window starts with a normal frame interval");
        double Follow(int frames)
        {
            double value = 0;
            for (int i = 0; i < frames; i++) value = HudMotion.Follow(value, 100, 1.0 / frames, 7);
            return value;
        }
        Check(Math.Abs(Follow(30) - Follow(60)) < 1e-10 && Math.Abs(Follow(60) - Follow(144)) < 1e-10, "motion has the same duration across different display rates");
        Check(HudMotion.Follow(0, 1, 0.1, 12) is > 0 and < 1 && HudMotion.Follow(0, 1, -1, 12) == 0, "smoothing cannot snap to its target during a dropped frame or move backwards");
        Console.WriteLine("All 9 motion checks passed.");
    }
    private static void CheckDuplicate(RenderFrameClock clock, TimeSpan time)
    {
        if (clock.Advance(time, out _)) throw new Exception("Duplicate rendering event was animated twice");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }
}
