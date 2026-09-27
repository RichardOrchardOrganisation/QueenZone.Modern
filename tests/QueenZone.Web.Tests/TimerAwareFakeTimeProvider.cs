namespace QueenZone.Web.Tests;

/// <summary>
/// A fake clock whose timers only fire when the test advances time, and which lets the test
/// await the Nth timer created against it. <see cref="PeriodicScopedHostedService"/> creates one
/// timer per wait (the startup delay, then one per run interval), so once timer N exists the
/// background loop has finished everything before its Nth wait. Tests advance fake time and
/// await that progress instead of sleeping on the real clock.
/// </summary>
internal sealed class TimerAwareFakeTimeProvider()
    : Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
{
    // Only guards against a hung loop failing the run forever; no test relies on it elapsing.
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> waiters = [];
    private int timersCreated;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        lock (gate)
        {
            timersCreated++;
            foreach (var waiter in waiters.Where(waiter => waiter.Count <= timersCreated).ToList())
            {
                waiters.Remove(waiter);
                waiter.Signal.TrySetResult();
            }
        }

        return timer;
    }

    /// <summary>Completes once at least <paramref name="count"/> timers have been created.</summary>
    public async Task WaitForTimersCreatedAsync(int count)
    {
        Task signalled;
        lock (gate)
        {
            if (timersCreated >= count)
            {
                return;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((count, signal));
            signalled = signal.Task;
        }

        try
        {
            await signalled.WaitAsync(HangTimeout);
        }
        catch (TimeoutException)
        {
            int created;
            lock (gate)
            {
                created = timersCreated;
            }

            throw new TimeoutException(
                $"Expected the background loop to reach wait #{count}, but only {created} timer(s) were created.");
        }
    }
}
