using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The debug sidecar's capture: the <see cref="SidecarFetcher"/> decorator recording requests,
/// documents, failures, and cancellation into the <see cref="SidecarStore"/>, plus the store's
/// eviction and the panel's IDE deep link.
/// </summary>
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

        await Assert.That(documents).Count().IsEqualTo(1);
        var entry = store.Entries.Single();
        await Assert.That(entry.Kind).IsEqualTo("query");
        await Assert.That(entry.Name).IsEqualTo("People");
        await Assert.That(entry.Query).IsEqualTo(request.Query);
        await Assert.That(entry.VariablesJson).Contains("\"id\": \"abc123\"");
        await Assert.That(entry.Headers.Single()).IsEqualTo(new("authorization", "Bearer token"));
        await Assert.That(entry.Documents.Single()).Contains("\"name\": \"Mark\"");
        await Assert.That(entry.DocumentCount).IsEqualTo(1);
        await Assert.That(entry.Completed).IsTrue();
        await Assert.That(entry.Cancelled).IsFalse();
        await Assert.That(entry.Error).IsNull();
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
        await Assert.That(entries[0].Kind).IsEqualTo("mutation");
        await Assert.That(entries[0].Name).IsEqualTo("<anonymous>");
        await Assert.That(entries[1].Kind).IsEqualTo("subscription");
        await Assert.That(entries[1].Name).IsEqualTo("OnGreeting");
        await Assert.That(entries[2].Kind).IsEqualTo("query");
        await Assert.That(entries[2].Name).IsEqualTo("<anonymous>");
    }

    /// <summary>
    /// The sidecar sits in front of the host app's own requests, so a document the parser refuses
    /// for its depth has to be recorded and passed through, not thrown back at the app.
    /// </summary>
    [Test]
    public async Task PassesADocumentTooDeepToParseThrough()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), store);

        var documents = await Drain(fetcher, new(DocumentInfoTests.TooDeep), noHeaders);

        await Assert.That(documents).Count().IsEqualTo(1);
        var entry = store.Entries.Single();
        await Assert.That(entry.Query).IsEqualTo(DocumentInfoTests.TooDeep);
        await Assert.That(entry.Name).IsEqualTo("<anonymous>");
    }

    [Test]
    public async Task RecordsFailureAndRethrows()
    {
        var store = NewStore();
        var fetcher = new SidecarFetcher(new ThrowingFetcher("boom"), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Drain(fetcher, new("{ id }"), noHeaders));

        var entry = store.Entries.Single();
        await Assert.That(entry.Completed).IsTrue();
        await Assert.That(entry.Error).IsEqualTo("boom");
        await Assert.That(entry.Cancelled).IsFalse();
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

        await Assert.That(caught).IsTrue();
        var entry = store.Entries.Single();
        await Assert.That(entry.Completed).IsTrue();
        await Assert.That(entry.Cancelled).IsTrue();
        await Assert.That(entry.Error).IsNull();
        await Assert.That(entry.Documents).Count().IsEqualTo(1);
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
        await Assert.That(entry.Completed).IsTrue();
        await Assert.That(entry.Cancelled).IsFalse();
        await Assert.That(entry.Error).IsNull();
        await Assert.That(entry.DocumentCount).IsEqualTo(1);
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
        await Assert.That(store.Entries.Select(_ => _.Name)).IsEquivalentTo(expected, CollectionOrdering.Matching);
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
        await Assert.That(documents).Count().IsEqualTo(3);
        var entry = store.Entries.Single();
        await Assert.That(entry.Documents).Count().IsEqualTo(2);
        await Assert.That(entry.DocumentCount).IsEqualTo(3);
    }

    [Test]
    public async Task DisabledCapturesNothing()
    {
        var store = NewStore(_ => _.Enabled = false);
        var fetcher = new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), store);

        var documents = await Drain(fetcher, new("{ id }"), noHeaders);

        await Assert.That(documents).Count().IsEqualTo(1);
        await Assert.That(store.Entries).IsEmpty();
    }

    [Test]
    public async Task ClearRaisesChangedAndEmpties()
    {
        var store = NewStore();
        var raised = 0;
        store.Changed += () => raised++;

        store.Clear();

        await Assert.That(store.Entries).IsEmpty();
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task IdeHrefRoundTripsThroughShareLink()
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
        await Assert.That(href).StartsWith("/ide#q=");

        var shared = ShareLinkCodec.TryDecode(href[href.IndexOf('#')..])!;
        await Assert.That(shared.Query).IsEqualTo(entry.Query);
        await Assert.That(shared.Variables).IsEqualTo(entry.VariablesJson);

        await Assert.That(BlazorQLSidecar.IdeHref(entry, "")).StartsWith("#q=");
        await Assert.That(BlazorQLSidecar.IdeHref(entry, null)).IsNull();
    }

    /// <summary>
    /// The store holds every captured request's headers — authorization included — and responses.
    /// Under Blazor Server one provider serves every user, each in a scope of their own (a circuit),
    /// so the store has to live in that scope: one shared across the process would show each user
    /// everyone's traffic. Under WebAssembly the app has the one scope, so nothing changes there.
    /// </summary>
    [Test]
    public async Task EachScopeHasAStoreOfItsOwn()
    {
        await using var provider = new ServiceCollection()
            .AddBlazorQLSidecar()
            .BuildServiceProvider(validateScopes: true);
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var store = first.ServiceProvider.GetRequiredService<SidecarStore>();

        await Assert.That(first.ServiceProvider.GetRequiredService<SidecarStore>()).IsSameReferenceAs(store);
        await Assert.That(second.ServiceProvider.GetRequiredService<SidecarStore>()).IsNotSameReferenceAs(store);
    }

    /// <summary>
    /// What a user sends is recorded in their store alone. The fetcher is resolved in the same scope
    /// as the panel, which is the registration the docs give.
    /// </summary>
    [Test]
    public async Task OneUsersRequestsNeverReachAnothersStore()
    {
        await using var provider = new ServiceCollection()
            .AddBlazorQLSidecar()
            .AddScoped<IGraphQLFetcher>(_ =>
                new SidecarFetcher(new FakeFetcher("""{"data":{}}"""), _.GetRequiredService<SidecarStore>()))
            .BuildServiceProvider(validateScopes: true);
        await using var alice = provider.CreateAsyncScope();
        await using var bob = provider.CreateAsyncScope();

        var fetcher = (SidecarFetcher) alice.ServiceProvider.GetRequiredService<IGraphQLFetcher>();
        await Drain(fetcher, new("{ secret }"), new Dictionary<string, string>
        {
            ["authorization"] = "Bearer alice"
        });

        await Assert.That(alice.ServiceProvider.GetRequiredService<SidecarStore>().Entries).Count().IsEqualTo(1);
        await Assert.That(bob.ServiceProvider.GetRequiredService<SidecarStore>().Entries).IsEmpty();
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
