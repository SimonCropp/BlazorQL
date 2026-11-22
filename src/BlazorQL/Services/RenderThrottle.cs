/// <summary>
/// Coalesces a stream of changes into at most one render per interval, without dropping the last
/// one. A change long enough after the previous render renders at once; one inside the interval
/// is left for a render scheduled at its end, which renders whatever has accumulated by then. So
/// a burst renders once per interval, and the state it ends on is always rendered, whether more
/// changes follow or the stream goes quiet.
/// </summary>
/// <remarks>
/// For a streamed response, where rendering is the cost that grows: each render serializes the
/// whole accumulated document and writes it to the editor, so rendering per part is quadratic in
/// the parts, and 2,000 @stream items took 1.4 s on desktop .NET before WebAssembly's slower
/// runtime is counted. Once per frame is as often as anyone can see a change anyway.
/// </remarks>
sealed class RenderThrottle(TimeSpan interval, Func<Task> render)
{
    long lastRender;
    bool dirty;
    bool scheduled;

    /// <summary>Notes a change, rendering now when the interval has passed since the last render.</summary>
    public Task Changed()
    {
        dirty = true;
        if (scheduled)
        {
            return Task.CompletedTask;
        }

        var elapsed = Stopwatch.GetElapsedTime(lastRender);
        if (lastRender == 0 ||
            elapsed >= interval)
        {
            return Flush();
        }

        scheduled = true;
        _ = RenderLater(interval - elapsed);
        return Task.CompletedTask;
    }

    /// <summary>Renders what has accumulated, if anything has. For the end of the stream.</summary>
    public Task Flush()
    {
        if (!dirty)
        {
            return Task.CompletedTask;
        }

        dirty = false;
        lastRender = Stopwatch.GetTimestamp();
        return render();
    }

    /// <summary>
    /// Forgets what has accumulated, so a render already scheduled finds nothing to do. For an
    /// outcome that replaces the stream's state, such as an error, which a late render would
    /// otherwise overwrite.
    /// </summary>
    public void Discard() =>
        dirty = false;

    async Task RenderLater(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            scheduled = false;
            await Flush();
        }
        catch (Exception exception)
        {
            // Nothing awaits this task, so the failure has nowhere else to go.
            await Console.Error.WriteLineAsync($"BlazorQL: a throttled render failed. {exception}");
        }
    }
}
