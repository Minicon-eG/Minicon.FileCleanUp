using Microsoft.Extensions.Time.Testing;
using System.Threading.Channels;

namespace Minicon.FileCleanUp.Tests;
// Advances only timers that have actually been registered, rather than racing the thread pool.
internal sealed class CoordinatedTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly FakeTimeProvider clock = new(start);
    private readonly Channel<TimeSpan> scheduled = Channel.CreateUnbounded<TimeSpan>();
    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
    public override long GetTimestamp() => clock.GetTimestamp();
    public override long TimestampFrequency => clock.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = clock.CreateTimer(callback, state, dueTime, period);
        if (dueTime >= TimeSpan.Zero)
        {
            scheduled.Writer.TryWrite(dueTime);
        }

        return timer;
    }

    public async Task Complete(Task operation)
    {
        while (!operation.IsCompleted)
        {
            var next = scheduled.Reader.WaitToReadAsync().AsTask();
            var winner = await Task.WhenAny(operation, next).WaitAsync(TimeSpan.FromSeconds(10));
            if (winner == operation)
            {
                break;
            }

            if (scheduled.Reader.TryRead(out var delay))
            {
                clock.Advance(delay);
            }
        }

        await operation;
    }
}
