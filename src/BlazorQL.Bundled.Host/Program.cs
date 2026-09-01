using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorQL.Bundled.Host;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// One root component and no router: BlazorQL.Bundled serves a single page, and everything
// configurable arrives as window.blazorqlConfig rather than through DI.
builder.RootComponents.Add<Shell>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
