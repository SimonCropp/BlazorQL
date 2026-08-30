namespace BlazorQL;

/// <summary>
/// Transports a GraphQL request and yields its result documents. One shape covers everything: a
/// query or mutation yields one document; incremental delivery (@defer/@stream) yields the initial
/// payload then patches; a subscription yields one document per event until cancelled.
/// </summary>
public interface IGraphQLFetcher
{
    IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancel);
}
