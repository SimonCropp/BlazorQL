/// <summary>
/// The debug sidecar's capture: the <see cref="SidecarFetcher"/> decorator recording requests,
/// documents, failures, and cancellation into the <see cref="SidecarStore"/>, plus the store's
/// eviction and the panel's IDE deep link.
/// </summary>
[TestFixture]
public class SidecarTests
{
    static readonly Dictionary<string, string> noHeaders = [];

    [Test]
    public async Task RecordsRequestAndDocuments()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(
            new FakeFetcher("""{"data":{"person":{"name":"Mark"}}}"""),
            store);

        var request = new GraphQLRequest(
            "query People($id: ID) { person(id: $id) { name } }",
            Variables("""{"id":"abc123"}"""));
        var headers = new Dictionary<string, string>
        {
            ["authorization"] = "Bearer token"
        };
        var documents = await Drain(fetcher, request, headers);

        Assert.That(documents, Has.Count.EqualTo(1));
        var entry = store.Entries.Single();
        Assert.That(entry.Kind, Is.EqualTo("query"));
        Assert.That(entry.Name, Is.EqualTo("People"));
        Assert.That(entry.Query, Is.EqualTo(request.Query));
        Assert.That(entry.VariablesJson, Does.Contain("\"id\": \"abc123\""));
        Assert.That(entry.Headers.Single(), Is.EqualTo(new KeyValuePair<string, string>("authorization", "Bearer token")));
        Assert.That(entry.Documents.Single(), Does.Contain("\"name\": \"Mark\""));
        Assert.That(entry.DocumentCount, Is.EqualTo(1));
        Assert.That(entry.Completed, Is.True);
        Assert.That(entry.Cancelled, Is.False);
        Assert.That(entry.Error, Is.Null);
    }

    [Test]
    public async Task DerivesKindAndAnonymousName()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), store);

        await Drain(fetcher, new("mutation { setName(name: \"Hi\") }"), noHeaders);
        await Drain(fetcher, new("subscription OnGreeting { greeting }"), noHeaders);
        await Drain(fetcher, new("this does not parse"), noHeaders);

        var entries = store.Entries;
        Assert.That(entries[0].Kind, Is.EqualTo("mutation"));
        Assert.That(entries[0].Name, Is.EqualTo("<anonymous>"));
        Assert.That(entries[1].Kind, Is.EqualTo("subscription"));
        Assert.That(entries[1].Name, Is.EqualTo("OnGreeting"));
        Assert.That(entries[2].Kind, Is.EqualTo("query"));
        Assert.That(entries[2].Name, Is.EqualTo("<anonymous>"));
    }

    [Test]
    public void RecordsFailureAndRethrows()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(new ThrowingFetcher("boom"), store);

        Assert.ThrowsAsync<InvalidOperationException>(() => Drain(fetcher, new("{ id }"), noHeaders));

        var entry = store.Entries.Single();
        Assert.That(entry.Completed, Is.True);
        Assert.That(entry.Error, Is.EqualTo("boom"));
        Assert.That(entry.Cancelled, Is.False);
    }

    [Test]
    public async Task CancellationMarksStopped()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(new HangingFetcher("""{"data":{"greeting":"Hi"}}"""), store);
        using var cancelSource = new CancelSource();

        var caught = false;
        try
        {
            await foreach (var _ in fetcher.FetchAsync(new("subscription { greeting }"), noHeaders, cancelSource.Token))
            {
                await cancelSource.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            caught = true;
        }

        Assert.That(caught, Is.True);
        var entry = store.Entries.Single();
        Assert.That(entry.Completed, Is.True);
        Assert.That(entry.Cancelled, Is.True);
        Assert.That(entry.Error, Is.Null);
        Assert.That(entry.Documents, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AbandonedStreamCompletesQuietly()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(
            new FakeFetcher("""{"data":{"n":1}}""", """{"data":{"n":2}}"""),
            store);

        await foreach (var _ in fetcher.FetchAsync(new("{ n }"), noHeaders, Cancel.None))
        {
            break;
        }

        var entry = store.Entries.Single();
        Assert.That(entry.Completed, Is.True);
        Assert.That(entry.Cancelled, Is.False);
        Assert.That(entry.Error, Is.Null);
        Assert.That(entry.DocumentCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EvictsOldestBeyondMaxEntries()
    {
        var store = NewStore(_ => _.MaxEntries = 2);
        var fetcher = new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), store);

        await Drain(fetcher, new("query First { id }"), noHeaders);
        await Drain(fetcher, new("query Second { id }"), noHeaders);
        await Drain(fetcher, new("query Third { id }"), noHeaders);

        string[] expected = ["Second", "Third"];
        Assert.That(store.Entries.Select(_ => _.Name), Is.EqualTo(expected));
    }

    [Test]
    public async Task TrimsDocumentsBeyondCap()
    {
        var store = NewStore(_ => _.MaxDocumentsPerEntry = 2);
        var fetcher = new SidecarFetcher(
            new FakeFetcher("""{"data":{"n":1}}""", """{"data":{"n":2}}""", """{"data":{"n":3}}"""),
            store);

        var documents = await Drain(fetcher, new("{ n }"), noHeaders);

        // The consumer still receives everything — only the log is capped.
        Assert.That(documents, Has.Count.EqualTo(3));
        var entry = store.Entries.Single();
        Assert.That(entry.Documents, Has.Count.EqualTo(2));
        Assert.That(entry.DocumentCount, Is.EqualTo(3));
    }

    [Test]
    public async Task DisabledCapturesNothing()
    {
        var store = NewStore(_ => _.Enabled = false);
        var fetcher = new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), store);

        var documents = await Drain(fetcher, new("{ id }"), noHeaders);

        Assert.That(documents, Has.Count.EqualTo(1));
        Assert.That(store.Entries, Is.Empty);
    }

    [Test]
    public void ClearRaisesChangedAndEmpties()
    {
        var store = NewStore();
        var raised = 0;
        store.Changed += () => raised++;

        store.Clear();

        Assert.That(store.Entries, Is.Empty);
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void IdeHrefRoundTripsThroughShareLink()
    {
        var entry = new SidecarEntry
        {
            Started = DateTimeOffset.Now,
            Query = "query People { person { name } }",
            VariablesJson = """{"id": "abc123"}""",
            Kind = "query",
            Name = "People",
            Headers = []
        };

        var href = BlazorQLSidecar.IdeHref(entry, "/ide")!;
        Assert.That(href, Does.StartWith("/ide#q="));

        var shared = ShareLinkCodec.TryDecode(href[href.IndexOf('#')..])!;
        Assert.That(shared.Query, Is.EqualTo(entry.Query));
        Assert.That(shared.Variables, Is.EqualTo(entry.VariablesJson));

        Assert.That(BlazorQLSidecar.IdeHref(entry, ""), Does.StartWith("#q="));
        Assert.That(BlazorQLSidecar.IdeHref(entry, null), Is.Null);
    }

    static SidecarStore NewStore(Action<SidecarOptions>? configure = null)
    {
        var options = new SidecarOptions();
        configure?.Invoke(options);
        return new(options);
    }

    static async Task<List<JsonElement>> Drain(
        SidecarFetcher fetcher,
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers)
    {
        var documents = new List<JsonElement>();
        await foreach (var document in fetcher.FetchAsync(request, headers, Cancel.None))
        {
            documents.Add(document);
        }

        return documents;
    }

    static JsonElement Variables(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    sealed class FakeFetcher(params string[] documents) :
        IGraphQLFetcher
    {
        public async IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            [EnumeratorCancellation] Cancel cancel)
        {
            foreach (var document in documents)
            {
                await Task.Yield();
                yield return Variables(document);
            }
        }
    }

    sealed class ThrowingFetcher(string message) :
        IGraphQLFetcher
    {
        public async IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            [EnumeratorCancellation] Cancel cancel)
        {
            await Task.Yield();
            // The condition keeps the trailing yield reachable to the compiler; it always throws.
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (message.Length >= 0)
            {
                throw new InvalidOperationException(message);
            }

            yield break;
        }
    }

    /// <summary>Yields its documents, then hangs until cancelled — a subscription's shape.</summary>
    sealed class HangingFetcher(params string[] documents) :
        IGraphQLFetcher
    {
        public async IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            [EnumeratorCancellation] Cancel cancel)
        {
            foreach (var document in documents)
            {
                await Task.Yield();
                yield return Variables(document);
            }

            await Task.Delay(Timeout.Infinite, cancel);
        }
    }
}
