namespace BlazorQL;

/// <summary>
/// Holds the requests the sidecar has captured. A singleton, deliberately apart from
/// <see cref="SidecarFetcher"/>: fetchers are swapped when the endpoint changes, and a log that
/// rotated with them would forget everything on every swap.
/// </summary>
public sealed class SidecarStore(SidecarOptions options)
{
    readonly object sync = new();
    readonly List<SidecarEntry> entries = [];
    int nextId;

    internal SidecarOptions Options { get; } = options;

    /// <summary>Raised after an entry is added or updated, or the log is cleared.</summary>
    public event Action? Changed;

    /// <summary>A snapshot of the captured entries, oldest first.</summary>
    public IReadOnlyList<SidecarEntry> Entries
    {
        get
        {
            lock (sync)
            {
                return [.. entries];
            }
        }
    }

    internal SidecarEntry Begin(SidecarEntry entry)
    {
        lock (sync)
        {
            entry.Id = ++nextId;
            entries.Add(entry);
            while (entries.Count > Options.MaxEntries)
            {
                entries.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        return entry;
    }

    /// <summary>Announces that an existing entry mutated — a document arrived, or it completed.</summary>
    internal void Notify() =>
        Changed?.Invoke();

    public void Clear()
    {
        lock (sync)
        {
            entries.Clear();
        }

        Changed?.Invoke();
    }
}
