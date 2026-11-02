namespace GlassPlayer;

// Composition timestamps identify actual render passes; a second wall-clock
// limiter can drop alternating 60 Hz frames when callbacks arrive slightly early.
public sealed class RenderFrameClock
{
    private TimeSpan? previous;
    public bool Advance(TimeSpan timestamp, out double delta)
    {
        delta = 0;
        if (previous == timestamp) return false;
        delta = previous is { } last && timestamp > last ? Math.Min((timestamp - last).TotalSeconds, 0.1) : 1.0 / 60;
        previous = timestamp;
        return true;
    }
    public void Reset() => previous = null;
}
