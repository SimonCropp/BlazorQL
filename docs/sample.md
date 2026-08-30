# The sample

The deployed sample is a standalone Blazor WebAssembly app with **no backend**: the schema is executed inside the WASM app by GraphQL.NET through the sample's `LocalSchemaFetcher`. It has two pages sharing one fetcher, registered in DI:

<!-- snippet: sampleFetcher -->
<a id='snippet-sampleFetcher'></a>
```cs
// The whole schema lives in the browser: GraphQL.NET executes it inside the WASM app itself, so
// the sample deploys to static hosting with subscriptions intact. Both pages resolve this one
// fetcher, and the sidecar decorator records every request — the sample app's and the query
// explorer's alike — into the debug panel.
builder.Services.AddSingleton<IGraphQLFetcher>(_ =>
    new SidecarFetcher(new LocalSchemaFetcher(), _.GetRequiredService<SidecarStore>()));
```
<sup><a href='/samples/BlazorQL.Sample/Program.cs#L15-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sampleFetcher' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## The app page

The default page is an ordinary Blazor app consuming that schema — a query run on load, a mutation behind a button, and a subscription streamed into a list:

<!-- snippet: homeQuery -->
<a id='snippet-homeQuery'></a>
```razor
// An ordinary app query through the shared fetcher — the sidecar records it like
// any other, alongside everything the query explorer sends.
var document = await QueryAsync(
    """
    query Profile {
      person {
        name
        age(delay: 21)
        friends {
          name
        }
      }
    }
    """);
```
<sup><a href='/samples/BlazorQL.Sample/Pages/Home.razor#L100-L115' title='Snippet source file'>snippet source</a> | <a href='#snippet-homeQuery' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Because every request goes through the shared fetcher, the [debug sidecar](sidecar.md) shows each one as it happens — which is the page's point: a realistic app to watch through the sidecar. A link in the header opens the query explorer.


## The query explorer

`/explorer` hosts the full BlazorQL IDE. Its first tab opens on a `Demo` query that exercises most of the schema — variables with defaults, an alias, several argument shapes, nested lists, and a union spread through a fragment — under a short comment explaining the page. It runs as-is.

Worth trying:

```graphql
subscription { message(delay: 300) }
```

```graphql
query { deferrable { normalString deferredString } }
```

The endpoint box above the IDE points the same UI at any real API — an `http(s)` url swaps in the HTTP fetcher, `ws(s)` the graphql-transport-ws fetcher, and clearing it returns to the in-browser schema.


## The schema

The schema itself is a C# port of GraphiQL's own test schema (`samples/BlazorQL.Sample/SampleSchema.cs` — original by GraphQL Contributors, MIT). It exercises the whole language: every scalar and list argument shape, enums and input objects with defaults, interfaces, unions, deprecated fields/values/arguments, markdown descriptions with images, and a real streaming subscription. GraphQL.NET has no incremental delivery, so unlike the graphql-js original the `@defer`/`@stream` directives are not part of the schema.

The browser test suite runs against this sample's **published output**, served both at the root and under a sub-path, so what the tests prove is exactly what GitHub Pages hosts. The screenshots in these docs are the suite's own verified baselines.
