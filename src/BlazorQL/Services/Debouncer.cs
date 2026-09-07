/// <summary>
/// Trailing-edge debounce over <see cref="Task.Delay(int, CancellationToken)"/>: each call resets
/// the timer, and only the last action within the window runs.
/// </summary>
sealed class Debouncer(int delayMs = 500) :
    IDisposable
{
    CancelSource? pending;

    public void Run(Func<Task> action)
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = new();
        _ = RunAfterDelay(action, pending.Token);
    }

    async Task RunAfterDelay(Func<Task> action, Cancel token)
    {
        try
        {
            await Task.Delay(delayMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // The window closed under the action. Nothing to report.
        }
        catch (Exception exception)
        {
            // Nothing awaits this task, so an exception here has nowhere else to go: a GetValue
            // after the editor was torn down, or a failed interop call, would otherwise be an
            // update that silently never happened.
            Console.Error.WriteLine($"BlazorQL: a debounced action failed. {exception}");
        }
    }

    /// <summary>
    /// Drops whatever is waiting, without ending the debouncer. For a caller that is about to do
    /// the pending action's work itself and would otherwise have it run again a moment later.
    /// </summary>
    public void Cancel()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }

    public void Dispose() =>
        Cancel();
}
