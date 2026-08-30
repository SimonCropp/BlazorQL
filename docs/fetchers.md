# Fetchers

A fetcher transports requests. One interface covers everything: a query or mutation yields one document, incremental delivery yields the initial payload then patches, and a subscription yields one document per event until cancelled.

<!-- snippet: fetcherInterface -->
<a id='snippet-fetcherInterface'></a>
```cs
public interface IGraphQLFetcher
{
    IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancel);
}
```
<sup><a href='/src/BlazorQL/Fetchers/IGraphQLFetcher.cs#L8-L16' title='Snippet source file'>snippet source</a> | <a href='#snippet-fetcherInterface' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The component also runs its introspection through the fetcher, so the schema always comes from the same place the queries go.


## HttpFetcher

```csharp
new HttpFetcher("https://example.com/graphql");
// or with a configured client (auth handlers, base address):
new HttpFetcher(httpClient, "/graphql");
```

POSTs JSON with `Accept: application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8`. A `multipart/mixed` response (incremental delivery) is streamed part by part; anything else is read as one document. The headers editor's entries ride on every request. The response's HTTP status code feeds the status line.


## GraphQLWsFetcher

```csharp
new GraphQLWsFetcher("wss://example.com/graphql");
```

Speaks the `graphql-transport-ws` subprotocol: `connection_init` carrying the headers editor's entries as the connection payload, then one subscribe per run; cancellation sends `complete`.


## In-browser schemas

A fetcher does not have to transport anything: the deployed sample's `LocalSchemaFetcher` (in `samples/BlazorQL.Sample`) executes a GraphQL.NET schema inside the WASM app itself — queries, mutations, and subscriptions with no server anywhere. Any schema that can run in the browser works the same way; see [the sample](sample.md).


## Writing a fetcher

Implement the interface and yield `JsonElement` documents. Yield once for a single result; yield repeatedly for a stream. Throw to surface a transport failure — the message lands in the response pane as a GraphQL-style error document. Honor the cancellation token: the stop button and tab switches cancel it.
