using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorQL;
using BlazorQL.Sample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// begin-snippet: sidecarRegistrationSample
// "Open in BlazorQL" on a captured request opens the query-explorer page.
builder.Services.AddBlazorQLSidecar(_ => _.IdeRoute = "explorer");
// end-snippet

// begin-snippet: sampleFetcher
// The whole schema lives in the browser: GraphQL.NET executes it inside the WASM app itself, so
// the sample deploys to static hosting with subscriptions intact. Both pages resolve this one
// fetcher, and the sidecar decorator records every request — the sample app's and the query
// explorer's alike — into the debug panel.
builder.Services.AddSingleton<IGraphQLFetcher>(_ =>
    new SidecarFetcher(new LocalSchemaFetcher(), _.GetRequiredService<SidecarStore>()));
// end-snippet

await builder.Build().RunAsync();
