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
    readonly Lock sync = new();
    readonly List<CapturedDocument> documents = [];

    /// <summary>
    /// A response as it arrived, pretty-printed the first time something reads it. Most entries
    /// are never opened, introspection among them, and pretty-printing up front turned a 6 MB
    /// schema into a 28 MB string held for as long as the entry was.
    /// </summary>
    sealed class CapturedDocument(JsonElement element)
    {
        string? pretty;

        // A benign race: two readers at once may both format it, and either result is the same.
        public string Pretty => pretty ??= Format(element);
    }

    static readonly JsonSerializerOptions prettyOptions = new()
    {
        WriteIndented = true
    };

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonElement has a built-in converter, so nothing here is discovered by reflection.")]
    internal static string Format(JsonElement element) =>
        JsonSerializer.Serialize(element, prettyOptions);
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

    /// <summary>
    /// The kept response documents, pretty-printed, oldest first. Each is formatted the first time
    /// it is read and kept from then on.
    /// </summary>
    public IReadOnlyList<string> Documents
    {
        get
        {
            CapturedDocument[] snapshot;
            lock (sync)
            {
                snapshot = [.. documents];
            }

            // Outside the lock: formatting a large response is the slow part, and a fetch still
            // appending to this entry has no reason to wait for it.
            return [.. snapshot.Select(_ => _.Pretty)];
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
    /// Records that a document arrived, keeping it only while under the cap. Kept as it arrived and
    /// formatted only when read, so a document nobody opens costs no text at all.
    /// </summary>
    internal void AddDocument(JsonElement document, int maxKept, TimeSpan elapsed)
    {
        lock (sync)
        {
            documentCount++;
            duration = elapsed;
            if (documents.Count < maxKept)
            {
                // Kept past the fetch, so it must not depend on a document the fetcher may dispose
                // once it moves on. Free for the built-in fetchers, which yield clones already: a
                // clone's clone is itself.
                documents.Add(new(document.Clone()));
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
