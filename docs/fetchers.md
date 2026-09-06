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
        Cancel cancel);
}
```
<sup><a href='/src/BlazorQL/Fetchers/IGraphQLFetcher.cs#L8-L16' title='Snippet source file'>snippet source</a> | <a href='#snippet-fetcherInterface' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The component also runs its introspection through the fetcher, with the headers editor's entries, so the schema always comes from the same place the queries go and an endpoint behind an `Authorization` header still has one.


## HttpFetcher

```csharp
new HttpFetcher("https://example.com/graphql");
// or with a configured client (auth handlers, base address):
new HttpFetcher(httpClient, "/graphql");
```

POSTs JSON with `Accept: application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8, text/event-stream;q=0.7`. A `multipart/mixed` response (incremental delivery) is streamed part by part, a `text/event-stream` response (subscriptions, below) event by event; anything else is read as one document. The headers editor's entries ride on every request: a content header such as `Content-Type` lands on the body, and an `Accept` typed there replaces the negotiated one rather than being appended to it. The response's HTTP status code feeds the status line.

The two streaming types come last, and in that order, because they are what the endpoint should fall back to rather than what it should reach for: a query the server can answer as one document is still answered as one document, and only an operation that cannot be takes it to the bottom of the list.


### Subscriptions over SSE

A subscription is an operation the server cannot answer as one document, so it picks the streaming type it supports and answers `text/event-stream` — GraphQL over SSE in distinct-connections mode. Nothing needs configuring: it is the same endpoint, the same fetcher, and the same POST that carries a query. `next` events land in the response pane as they arrive, `complete` ends the run, and comment keep-alives are ignored. Stopping the run closes the connection, which is how the protocol says a subscription ends.

This is what a Hot Chocolate server serves out of the box, so the one `HttpFetcher` above covers queries, mutations, and subscriptions against it.

Use `GraphQLWsFetcher` instead when the server offers only websockets. When subscriptions live at a url of their own, `new SplitFetcher(other, subscriptions)` routes them there and everything else to `other`.


## GraphQLWsFetcher

```csharp
new GraphQLWsFetcher("wss://example.com/graphql");
```

Speaks the `graphql-transport-ws` subprotocol: `connection_init` carrying the headers editor's entries as the connection payload, then one subscribe per run; cancellation sends `complete`.


## In-browser schemas

A fetcher does not have to transport anything: the deployed sample's `LocalSchemaFetcher` (in `samples/BlazorQL.Sample`) executes a GraphQL.NET schema inside the WASM app itself — queries, mutations, and subscriptions with no server anywhere. Any schema that can run in the browser works the same way; see [the sample](sample.md).


## Writing a fetcher

Implement the interface and yield `JsonElement` documents. Yield once for a single result; yield repeatedly for a stream. Throw to surface a transport failure — the message lands in the response pane as a GraphQL-style error document. Honor the cancellation token: the stop button and tab switches cancel it.
