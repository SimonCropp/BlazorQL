namespace BlazorQL;

/// <summary>One captured GraphQL request, as recorded by <see cref="SidecarFetcher"/>.</summary>
/// <remarks>
/// An entry is not immutable the way a plain HTTP log line would be: a fetch yields documents over
/// time — an initial payload then patches, or one event per subscription message — so the entry
/// accumulates them and completes when the enumeration ends. Reads snapshot under a lock, so a
/// render never observes a half-appended list.
/// </remarks>
public sealed class SidecarEntry
{
    readonly object sync = new();
    readonly List<string> documents = [];
    int documentCount;
    TimeSpan duration;
    bool completed;
    bool cancelled;
    string? error;

    public int Id { get; internal set; }

    public required DateTimeOffset Started { get; init; }

    /// <summary>The operation text exactly as the fetcher sent it.</summary>
    public required string Query { get; init; }

    /// <summary>The request's explicit operation name, when it carried one.</summary>
    public string? OperationName { get; init; }

    /// <summary>The variables pretty-printed as JSON; null when the request carried none.</summary>
    public string? VariablesJson { get; init; }

    /// <summary>
    /// The operation kind — <c>query</c>, <c>mutation</c>, or <c>subscription</c> — parsed from
    /// the operation text. Text that does not parse reports <c>query</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>The display name: the operation's name, or <c>&lt;anonymous&gt;</c>.</summary>
    public required string Name { get; init; }

    public required IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; }

    /// <summary>The kept response documents, pretty-printed, oldest first.</summary>
    public IReadOnlyList<string> Documents
    {
        get
        {
            lock (sync)
            {
                return [.. documents];
            }
        }
    }

    /// <summary>
    /// Every document the fetch yielded — larger than <see cref="Documents"/> when the per-entry
    /// cap trimmed the tail.
    /// </summary>
    public int DocumentCount
    {
        get
        {
            lock (sync)
            {
                return documentCount;
            }
        }
    }

    /// <summary>Elapsed so far while the fetch runs; the total once it completes.</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (sync)
            {
                return duration;
            }
        }
    }

    /// <summary>Whether the fetch has ended — completed, stopped, or failed.</summary>
    public bool Completed
    {
        get
        {
            lock (sync)
            {
                return completed;
            }
        }
    }

    /// <summary>
    /// Whether the fetch ended by cancellation — the normal way a subscription stops, so it is
    /// kept apart from <see cref="Error"/>.
    /// </summary>
    public bool Cancelled
    {
        get
        {
            lock (sync)
            {
                return cancelled;
            }
        }
    }

    /// <summary>The transport exception's message, when the fetch failed.</summary>
    public string? Error
    {
        get
        {
            lock (sync)
            {
                return error;
            }
        }
    }

    /// <summary>
    /// Records that a document arrived, keeping its text only while under the cap.
    /// <paramref name="render"/> is a function rather than the text because rendering one the cap
    /// will drop is waste a long subscription would pay for on every event.
    /// </summary>
    internal void AddDocument(Func<string> render, int maxKept, TimeSpan elapsed)
    {
        bool keep;
        lock (sync)
        {
            documentCount++;
            duration = elapsed;
            keep = documents.Count < maxKept;
        }

        if (!keep)
        {
            return;
        }

        var json = render();
        lock (sync)
        {
            // Re-checked: something else may have taken the last slot in between.
            if (documents.Count < maxKept)
            {
                documents.Add(json);
            }
        }
    }

    internal void Complete(TimeSpan elapsed, bool wasCancelled, string? failure)
    {
        lock (sync)
        {
            duration = elapsed;
            completed = true;
            cancelled = wasCancelled;
            error = failure;
        }
    }
}
