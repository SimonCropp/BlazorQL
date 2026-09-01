namespace BlazorQL;

/// <summary>
/// Routes subscriptions to one fetcher and everything else to another, so an IDE can post queries
/// and mutations over http while subscriptions ride a websocket — the pairing GraphiQL exposes as
/// <c>url</c> plus <c>subscriptionUrl</c>, and the usual shape of a real server.
/// </summary>
/// <remarks>
/// The document is parsed rather than string-matched, so a query whose name or a field's name
/// happens to contain "subscription" is not mistaken for one. A document that will not parse goes
/// to <paramref name="other"/>, which is where the resulting error is reported best.
/// </remarks>
public sealed class SplitFetcher(IGraphQLFetcher other, IGraphQLFetcher subscriptions) :
    IGraphQLFetcher
{
    /// <summary>The fetcher queries and mutations go to.</summary>
    public IGraphQLFetcher Other { get; } = other;

    /// <summary>The fetcher subscriptions go to.</summary>
    public IGraphQLFetcher Subscriptions { get; } = subscriptions;

    public IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        Cancel cancel)
    {
        var fetcher = IsSubscription(request) ? Subscriptions : Other;
        return fetcher.FetchAsync(request, headers, cancel);
    }

    static bool IsSubscription(GraphQLRequest request)
    {
        GraphQLDocument document;
        try
        {
            document = Parser.Parse(request.Query);
        }
        catch (GraphQLSyntaxErrorException)
        {
            return false;
        }

        var operations = document.Definitions.OfType<GraphQLOperationDefinition>().ToList();

        // With a name the editor picked, honour it; the document may hold several operations and
        // only one of them runs.
        if (request.OperationName is {Length: > 0} name)
        {
            var named = operations.FirstOrDefault(_ => _.Name?.StringValue == name);
            return named?.Operation == OperationType.Subscription;
        }

        return operations is [{Operation: OperationType.Subscription}];
    }
}
