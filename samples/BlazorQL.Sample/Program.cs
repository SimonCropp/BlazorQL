using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using BlazorQL;
using BlazorQL.Sample;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// begin-snippet: sidecarRegistrationSample
builder.Services.AddBlazorQLSidecar();
// end-snippet

await builder.Build().RunAsync();
