namespace RadarrMcp.Tests;

/// <summary>
/// Fake clock: every timer (and therefore every Task.Delay) advances time by its due time and fires
/// right away, so polling code runs without really sleeping. Records the requested delays.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private TimeSpan _now;

    public List<TimeSpan> Delays { get; } = [];

    public TimeSpan Elapsed { get { lock (_lock) return _now; } }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Elapsed.Ticks;

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + Elapsed;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_lock)
        {
            Delays.Add(dueTime);
            _now += dueTime;
        }
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new FiredTimer();
    }

    private sealed class FiredTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
