/// <summary>
/// Routing by operation type. The routing decision is public because the IDE's status footer has to
/// ask the same question — after the fact it would read the other fetcher's leftovers.
/// </summary>
public class SplitFetcherTests
{
    static readonly RecordingFetcher other = new();
    static readonly RecordingFetcher subscriptions = new();
    static readonly SplitFetcher split = new(other, subscriptions);

    [Test]
    public async Task AQueryGoesToTheOtherFetcher() =>
        await Assert.That(split.For(new("{ id }"))).IsSameReferenceAs(other);

    [Test]
    public async Task AMutationGoesToTheOtherFetcher() =>
        await Assert.That(split.For(new("mutation { save }"))).IsSameReferenceAs(other);

    [Test]
    public async Task ASubscriptionGoesToTheSubscriptionFetcher() =>
        await Assert.That(split.For(new("subscription { message }"))).IsSameReferenceAs(subscriptions);

    /// <summary>The document can hold several operations; only the named one runs.</summary>
    [Test]
    public async Task TheNamedOperationDecides()
    {
        const string document = "query Q { id } subscription S { message }";

        await Assert.That(split.For(new(document, OperationName: "S"))).IsSameReferenceAs(subscriptions);
        await Assert.That(split.For(new(document, OperationName: "Q"))).IsSameReferenceAs(other);
    }

    /// <summary>A name that merely contains "subscription" is not one.</summary>
    [Test]
    public async Task AQueryNamedAfterSubscriptionsIsStillAQuery() =>
        await Assert.That(split.For(new("query subscriptionCount { id }"))).IsSameReferenceAs(other);

    /// <summary>A document that will not parse goes where its error is reported best.</summary>
    [Test]
    public async Task AnUnparseableDocumentGoesToTheOtherFetcher() =>
        await Assert.That(split.For(new("subscription {"))).IsSameReferenceAs(other);

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