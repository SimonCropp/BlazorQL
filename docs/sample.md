# The sample

The deployed sample is a standalone Blazor WebAssembly app with **no backend**: the schema is executed in the browser by graphql-js through the `LocalSchemaFetcher`.

<!-- snippet: sampleFetcher -->
<a id='snippet-sampleFetcher'></a>
```razor
// The whole schema lives in the browser by default: graphql-js executes it inside the page, so
// the sample deploys to static hosting with subscriptions and incremental delivery intact.
IGraphQLFetcher fetcher = new LocalSchemaFetcher(localSchemaUrl);
```
<sup><a href='/samples/BlazorQL.Sample/App.razor#L18-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sampleFetcher' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The schema itself is GraphiQL's own test schema, copied verbatim (`samples/BlazorQL.Sample/wwwroot/test-schema/` — MIT, GraphQL Contributors). It exercises the whole language: every scalar and list argument shape, enums and input objects with defaults, interfaces, unions, deprecated fields/values/arguments, markdown descriptions with images, a subscription driven by an async generator, and `@defer`/`@stream` fields.

Worth trying:

```graphql
subscription { message(delay: 300) }
```

```graphql
query { deferrable { normalString ... @defer { deferredString } } }
```

The endpoint box above the IDE points the same UI at any real API — an `http(s)` url swaps in the HTTP fetcher, `ws(s)` the graphql-transport-ws fetcher, and clearing it returns to the in-browser schema.

The browser test suite runs against this sample's **published output**, served both at the root and under a sub-path, so what the tests prove is exactly what GitHub Pages hosts. The screenshots in these docs are the suite's own verified baselines.
