using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// The IDE behind a proxy that strips a prefix without saying so, which is what
/// <see cref="BlazorQLIdeOptions.BasePathOverride"/> is for. The app sees <c>/blazorql</c> while the
/// browser is at <c>/api/blazorql</c>, so anything sent back to the browser has to use the public
/// path rather than the one the app was asked for.
/// </summary>
public class BasePathOverrideTests
{
    static readonly Lazy<Task<WebApplication>> host = new(StartHost);

    static async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.MapBlazorQL(
            _ =>
            {
                _.Endpoint = "/graphql";
                _.BasePathOverride = "/api/blazorql";
            });
        // Written with its trailing slash, which the redirect must not double.
        app.MapBlazorQL(
            "/slashed",
            _ =>
            {
                _.Endpoint = "/graphql";
                _.BasePathOverride = "/api/slashed/";
            });

        await app.StartAsync();
        return app;
    }

    static async Task<HttpResponseMessage> Get(string path)
    {
        var app = await host.Value;
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler);
        return await client.GetAsync(app.Urls.Single().TrimEnd('/') + path);
    }

    [Test]
    public async Task TheBaseHrefIsTheOverride()
    {
        var response = await Get("/blazorql/");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("""<base href="/api/blazorql/" />""");
    }

    /// <summary>
    /// Redirecting to the path the app saw would send the browser to <c>/blazorql/</c>, outside the
    /// prefix the proxy serves it under.
    /// </summary>
    [Test]
    public async Task TheBareMountRedirectsToTheOverride()
    {
        var response = await Get("/blazorql?x=1");

        await Assert.That((int) response.StatusCode).IsBetween(300, 399);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/api/blazorql/?x=1");
    }

    [Test]
    public async Task AnOverrideWithATrailingSlashRedirectsOnce()
    {
        var response = await Get("/slashed");

        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/api/slashed/");
    }
}
