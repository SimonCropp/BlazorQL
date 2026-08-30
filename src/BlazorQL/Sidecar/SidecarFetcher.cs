namespace BlazorQL;

/// <summary>
/// Records every request through an inner fetcher into a <see cref="SidecarStore"/>, for the
/// <see cref="BlazorQLSidecar"/> panel. Wraps any <see cref="IGraphQLFetcher"/> and changes
/// nothing about what flows through it — every document is yielded onward exactly as received,
/// and failures still propagate after being noted.
/// </summary>
public sealed class SidecarFetcher(IGraphQLFetcher inner, SidecarStore store) :
    IGraphQLFetcher
{
    static readonly JsonSerializerOptions prettyOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// The wrapped fetcher, for callers that key behavior off the concrete transport (the IDE's
    /// footer reads <see cref="HttpFetcher.LastStatus"/> through this).
    /// </summary>
    public IGraphQLFetcher Inner { get; } = inner;

    public async IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] Cancel cancel)
    {
        if (!store.Options.Enabled)
        {
            await foreach (var document in Inner.FetchAsync(request, headers, cancel))
            {
                yield return document;
            }

            yield break;
        }

        var entry = store.Begin(Describe(request, headers));
        var stopwatch = Stopwatch.StartNew();
        var enumerator = Inner.FetchAsync(request, headers, cancel).GetAsyncEnumerator(cancel);
        try
        {
            while (true)
            {
                JsonElement current;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    current = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    entry.Complete(stopwatch.Elapsed, wasCancelled: true, failure: null);
                    store.Notify();
                    throw;
                }
                catch (Exception exception)
                {
                    entry.Complete(stopwatch.Elapsed, wasCancelled: false, failure: exception.Message);
                    store.Notify();
                    throw;
                }

                entry.AddDocument(Pretty(current), store.Options.MaxDocumentsPerEntry, stopwatch.Elapsed);
                store.Notify();
                yield return current;
            }

            entry.Complete(stopwatch.Elapsed, wasCancelled: false, failure: null);
            store.Notify();
        }
        finally
        {
            // A consumer abandoning the stream early lands here without the loop having
            // completed the entry. Taking what was needed and disposing is a normal way to
            // finish — the IDE's introspection does exactly that — so it closes as completed;
            // only cancellation (the catch above) reports as stopped.
            if (!entry.Completed)
            {
                entry.Complete(stopwatch.Elapsed, wasCancelled: false, failure: null);
                store.Notify();
            }

            await enumerator.DisposeAsync();
        }
    }

    static SidecarEntry Describe(GraphQLRequest request, IReadOnlyDictionary<string, string> headers)
    {
        var info = DocumentInfo.Parse(request.Query);
        var operation = info.OperationNode(request.OperationName);
        return new()
        {
            Started = DateTimeOffset.Now,
            Query = request.Query,
            OperationName = request.OperationName,
            VariablesJson = request.Variables is { } variables
                ? Pretty(variables)
                : null,
            Kind = operation?.Operation.ToString().ToLowerInvariant() ?? "query",
            Name = request.OperationName ?? operation?.Name?.StringValue ?? "<anonymous>",
            Headers = [.. headers]
        };
    }

    static string Pretty(JsonElement element) =>
        JsonSerializer.Serialize(element, prettyOptions);
}
