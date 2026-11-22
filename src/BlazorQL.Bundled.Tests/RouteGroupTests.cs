using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// The IDE mounted inside route groups — <c>app.MapGroup("/admin").RequireAuthorization()
/// .MapBlazorQL()</c> is the ordinary way to put it behind a policy. A group adds to the routes
/// without touching PathBase, so everything derived from the mount has to come from the route that
/// matched rather than from the pattern MapBlazorQL was handed.
/// </summary>
public class RouteGroupTests
{
    static readonly Lazy<Task<WebApplication>> host = new(StartHost);

    static async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.MapGroup("/admin").MapBlazorQL(_ => _.Endpoint = "/graphql");
        // The same default mount in a second group of the same app: only the group tells them apart.
        app.MapGroup("/ops").MapBlazorQL(_ => _.Endpoint = "/graphql");
        app.MapGroup("/tenants/{tenant}").MapBlazorQL("/ide", _ => _.Endpoint = "/graphql");

        await app.StartAsync();
        return app;
    }

    static async Task<(HttpResponseMessage Response, WebApplication App)> Get(string path)
    {
        var app = await host.Value;
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler);
        return (await client.GetAsync(app.Urls.Single().TrimEnd('/') + path), app);
    }

    static async Task<string> Page(string path)
    {
        var (response, _) = await Get(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    static string Application(WebApplication app) =>
        app.Services.GetRequiredService<IHostEnvironment>().ApplicationName;

    [Test]
    public async Task TheBaseHrefIncludesTheGroup() =>
        await Assert.That(await Page("/admin/blazorql/")).Contains("""<base href="/admin/blazorql/" />""");

    /// <summary>A deep link served by the catch-all resolves the same mount as the mount itself.</summary>
    [Test]
    public async Task AnAssetRouteResolvesTheSameMount()
    {
        var (response, _) = await Get("/admin/blazorql/_framework/blazor.webassembly.js");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task TheBareMountRedirectsInsideTheGroup()
    {
        var (response, _) = await Get("/admin/blazorql?x=1");

        await Assert.That((int) response.StatusCode).IsBetween(300, 399);
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/admin/blazorql/?x=1");
    }

    /// <summary>A group's route parameter takes the value the request matched it with.</summary>
    [Test]
    public async Task AGroupParameterIsFilledFromTheRequest()
    {
        await Assert.That(await Page("/tenants/acme/ide/")).Contains("""<base href="/tenants/acme/ide/" />""");

        var (response, _) = await Get("/tenants/acme/ide");
        await Assert.That(response.Headers.Location!.ToString()).IsEqualTo("/tenants/acme/ide/");
    }

    /// <summary>
    /// Two groups taking the default mount in one app are two IDEs, and localStorage is shared by the
    /// whole origin — only the group keeps their tabs and history apart.
    /// </summary>
    [Test]
    public async Task EachGroupHasAStorageNamespaceOfItsOwn()
    {
        var (_, app) = await Get("/");
        var application = Application(app);

        await Assert.That(await Page("/admin/blazorql/")).Contains($"""
            "storageNamespace":"blazorql/{application}/admin/blazorql"
            """);
        await Assert.That(await Page("/ops/blazorql/")).Contains($"""
            "storageNamespace":"blazorql/{application}/ops/blazorql"
            """);
    }

    /// <summary>
    /// The namespace names the mount's pattern rather than the values it matched, the same rule as
    /// <see cref="IdeEndpointTests.UnknownPathsUnderAMountShareItsNamespace"/>: it must not move
    /// with the url.
    /// </summary>
    [Test]
    public async Task AParameterisedGroupNamespacesByItsPattern()
    {
        var (_, app) = await Get("/");

        await Assert.That(await Page("/tenants/acme/ide/")).Contains($$"""
            "storageNamespace":"blazorql/{{Application(app)}}/tenants/{tenant}/ide"
            """);
    }
}
