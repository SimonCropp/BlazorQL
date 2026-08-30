/// <summary>
/// Trailing-edge debounce over <see cref="Task.Delay(int, CancellationToken)"/>: each call resets
/// the timer, and only the last action within the window runs.
/// </summary>
sealed class Debouncer(int delayMs = 500) :
    IDisposable
{
    CancellationTokenSource? pending;

    public void Run(Func<Task> action)
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        _ = RunAfterDelay(action, pending.Token);
    }

    async Task RunAfterDelay(Func<Task> action, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await action();
        }
    }

    public void Dispose()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }
}
