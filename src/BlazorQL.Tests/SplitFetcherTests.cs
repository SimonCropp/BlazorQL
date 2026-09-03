/// <summary>
/// Routing by operation type. The routing decision is public because the IDE's status footer has to
/// ask the same question — after the fact it would read the other fetcher's leftovers.
/// </summary>
[TestFixture]
public class SplitFetcherTests
{
    static readonly RecordingFetcher other = new();
    static readonly RecordingFetcher subscriptions = new();
    static readonly SplitFetcher split = new(other, subscriptions);

    [Test]
    public void AQueryGoesToTheOtherFetcher() =>
        Assert.That(split.For(new("{ id }")), Is.SameAs(other));

    [Test]
    public void AMutationGoesToTheOtherFetcher() =>
        Assert.That(split.For(new("mutation { save }")), Is.SameAs(other));

    [Test]
    public void ASubscriptionGoesToTheSubscriptionFetcher() =>
        Assert.That(split.For(new("subscription { message }")), Is.SameAs(subscriptions));

    /// <summary>The document can hold several operations; only the named one runs.</summary>
    [Test]
    public void TheNamedOperationDecides()
    {
        const string document = "query Q { id } subscription S { message }";

        Assert.That(split.For(new(document, OperationName: "S")), Is.SameAs(subscriptions));
        Assert.That(split.For(new(document, OperationName: "Q")), Is.SameAs(other));
    }

    /// <summary>A name that merely contains "subscription" is not one.</summary>
    [Test]
    public void AQueryNamedAfterSubscriptionsIsStillAQuery() =>
        Assert.That(split.For(new("query subscriptionCount { id }")), Is.SameAs(other));

    /// <summary>A document that will not parse goes where its error is reported best.</summary>
    [Test]
    public void AnUnparseableDocumentGoesToTheOtherFetcher() =>
        Assert.That(split.For(new("subscription {")), Is.SameAs(other));

    sealed class RecordingFetcher :
        IGraphQLFetcher
    {
        public IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            Cancel cancel) =>
            AsyncEnumerable.Empty<JsonElement>();
    }
}
