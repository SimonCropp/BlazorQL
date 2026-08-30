# The sample

The deployed sample is a standalone Blazor WebAssembly app with **no backend**: the schema is executed inside the WASM app by GraphQL.NET through the sample's `LocalSchemaFetcher`.

<!-- snippet: sampleFetcher -->
<a id='snippet-sampleFetcher'></a>
```razor
// The whole schema lives in the browser by default: GraphQL.NET executes it inside the WASM
// app itself, so the sample deploys to static hosting with subscriptions intact. The sidecar
// decorator records every request the IDE makes into the debug panel.
protected override void OnInitialized() =>
    fetcher = new SidecarFetcher(new LocalSchemaFetcher(), Sidecar);
```
<sup><a href='/samples/BlazorQL.Sample/App.razor#L22-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sampleFetcher' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The schema itself is a C# port of GraphiQL's own test schema (`samples/BlazorQL.Sample/SampleSchema.cs` — original by GraphQL Contributors, MIT). It exercises the whole language: every scalar and list argument shape, enums and input objects with defaults, interfaces, unions, deprecated fields/values/arguments, markdown descriptions with images, and a real streaming subscription. GraphQL.NET has no incremental delivery, so unlike the graphql-js original the `@defer`/`@stream` directives are not part of the schema.

The first tab opens on a `Demo` query that exercises most of that schema — variables with defaults, an alias, several argument shapes, nested lists, and a union spread through a fragment — under a short comment explaining the page. It runs as-is.

Worth trying:

```graphql
subscription { message(delay: 300) }
```

```graphql
query { deferrable { normalString deferredString } }
```

The endpoint box above the IDE points the same UI at any real API — an `http(s)` url swaps in the HTTP fetcher, `ws(s)` the graphql-transport-ws fetcher, and clearing it returns to the in-browser schema.

The browser test suite runs against this sample's **published output**, served both at the root and under a sub-path, so what the tests prove is exactly what GitHub Pages hosts. The screenshots in these docs are the suite's own verified baselines.
