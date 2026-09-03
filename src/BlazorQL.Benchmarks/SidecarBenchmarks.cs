/// <summary>
/// The debug sidecar's cost to the requests it watches. A long subscription is the case that
/// matters: the fetcher sees an event, the store notifies, and the panel renders — over and over,
/// long after the log has stopped keeping anything.
/// </summary>
[MemoryDiagnoser]
public class SidecarBenchmarks
{
    static readonly Dictionary<string, string> noHeaders = [];

    JsonDocument document = null!;
    SidecarStore store = null!;
    IGraphQLFetcher fetcher = null!;

    /// <summary>Events in the subscription. The store keeps the first 25 by default.</summary>
    [Params(200)]
    public int Events { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var body = new string('x', 4000);
        document = JsonDocument.Parse(
            """{"data":{"message":{"id":"1","body":"BODY"}}}""".Replace("BODY", body, StringComparison.Ordinal));

        store = new(new());
        fetcher = new SidecarFetcher(new RepeatingFetcher(document.RootElement, Events), store);
    }

    [GlobalCleanup]
    public void Cleanup() =>
        document.Dispose();

    [IterationSetup]
    public void ClearLog() =>
        store.Clear();

    [Benchmark]
    public async Task<int> CaptureASubscription()
    {
        var seen = 0;
        await foreach (var _ in fetcher.FetchAsync(new("subscription { message { id body } }"), noHeaders, default))
        {
            seen++;
        }

        return seen;
    }

    /// <summary>What the panel asks of the store for one render, four times over as it used to.</summary>
    [Benchmark]
    public int SnapshotTheLog() =>
        store.Entries.Count;

    sealed class RepeatingFetcher(JsonElement payload, int count) :
        IGraphQLFetcher
    {
        public async IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            [EnumeratorCancellation] Cancel cancel)
        {
            for (var index = 0; index < count; index++)
            {
                await Task.Yield();
                yield return payload;
            }
        }
    }
}
