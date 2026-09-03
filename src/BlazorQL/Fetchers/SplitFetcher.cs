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

    /// <summary>
    /// Which of the two a request goes to. Public because a caller that keys off the concrete
    /// transport — the IDE's status footer reads <see cref="HttpFetcher.LastStatus"/> — has to ask
    /// the same question this does, and asking after the fact would read the other one's leftovers.
    /// </summary>
    public IGraphQLFetcher For(GraphQLRequest request) =>
        IsSubscription(request) ? Subscriptions : Other;

    public IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        Cancel cancel) =>
        For(request).FetchAsync(request, headers, cancel);

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
