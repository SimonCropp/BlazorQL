/// <summary>
/// Writes tab state to storage, on one of two paths.
/// </summary>
/// <remarks>
/// The editors already coalesce their own change bursts before they touch tab state, so putting a
/// second window in front of the write only delays it: a keystroke's state reached storage one
/// window plus one window after the typing stopped, not one. <see cref="Now"/> is that path.
/// <para>
/// Everything else - adding, closing, reordering or renaming a tab, toggling header persistence -
/// arrives straight from a click with nothing in front of it, and several of those can land
/// together. <see cref="Soon"/> keeps the window for them.
/// </para>
/// </remarks>
sealed class StatePersister(Action write, int delayMs = 500) :
    IDisposable
{
    readonly Debouncer debounce = new(delayMs);

    /// <summary>Writes now, for a caller that has already waited out a window of its own.</summary>
    public void Now()
    {
        // A write already waiting would repeat this one against unchanged state.
        debounce.Cancel();
        write();
    }

    /// <summary>Writes once the calls stop, for a caller that may be one of a burst.</summary>
    public void Soon() =>
        debounce.Run(() =>
        {
            write();
            return Task.CompletedTask;
        });

    public void Dispose() =>
        debounce.Dispose();
}
