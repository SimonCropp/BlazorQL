/// <summary>
/// The rendered index cache, exercised without a server. Behind UseForwardedHeaders honouring
/// X-Forwarded-Prefix from an untrusted hop, the base href a request resolves to is the client's to
/// choose, so the map it keys has to be bounded.
/// </summary>
public class IdeEndpointTests
{
    static async Task<string> Render(IdeEndpoint endpoint, string pathBase)
    {
        var context = new DefaultHttpContext
        {
            Request =
            {
                PathBase = pathBase
            }
        };
        var body = new MemoryStream();
        context.Response.Body = body;

        await endpoint.WriteIndex(context);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Test]
    public async Task ThePageCarriesTheRequestsBaseHref()
    {
        var endpoint = new IdeEndpoint(new(), "/blazorql", "Orders");

        await Assert.That(await Render(endpoint, "/one")).Contains("""<base href="/one/blazorql/" />""");
        await Assert.That(await Render(endpoint, "/two")).Contains("""<base href="/two/blazorql/" />""");
        await Assert.That(endpoint.CachedPages).IsEqualTo(2);
    }

    /// <summary>Renders are cached, so the same base path does not pay twice.</summary>
    [Test]
    public async Task ARepeatedBasePathAddsNothing()
    {
        var endpoint = new IdeEndpoint(new(), "/blazorql", "Orders");

        await Render(endpoint, "/same");
        await Render(endpoint, "/same");

        await Assert.That(endpoint.CachedPages).IsEqualTo(1);
    }

    [Test]
    public async Task ThousandsOfBasePathsDoNotGrowTheCacheWithoutLimit()
    {
        var endpoint = new IdeEndpoint(new(), "/blazorql", "Orders");

        for (var index = 0; index < 2000; index++)
        {
            await Render(endpoint, $"/prefix{index}");
        }

        await Assert.That(endpoint.CachedPages).IsLessThanOrEqualTo(32);

        // And every one of them still got its own base href, cached or not.
        await Assert.That(await Render(endpoint, "/prefix1999")).Contains("""<base href="/prefix1999/blazorql/" />""");
    }

    /// <summary>
    /// localStorage is scoped to an origin and not to a path, so what separates two IDEs a host has
    /// served is the app that mounted each one, and then the mount within that app.
    /// </summary>
    [Test]
    [Arguments("Orders", "", "blazorql/Orders")]
    [Arguments("Orders", "/blazorql", "blazorql/Orders/blazorql")]
    [Arguments("Orders", "/admin/graphql", "blazorql/Orders/admin/graphql")]
    // Two apps that both take the default mount, the case the mount alone cannot separate.
    [Arguments("Billing", "/blazorql", "blazorql/Billing/blazorql")]
    // No entry assembly to name the app after. The mount still separates what it can.
    [Arguments("", "/blazorql", "blazorql/blazorql")]
    public async Task TheAppAndMountNamespaceStorage(string application, string prefix, string expected)
    {
        var endpoint = new IdeEndpoint(new(), prefix, application);

        await Assert.That(await Render(endpoint, "")).Contains($"""
            "storageNamespace":"{expected}"
            """);
    }

    /// <summary>A named namespace has to win over the derived one.</summary>
    [Test]
    public async Task ANamedStorageNamespaceWinsOverTheMount()
    {
        var options = new BlazorQLIdeOptions
        {
            StorageNamespace = "orders"
        };
        var endpoint = new IdeEndpoint(options, "/blazorql", "Orders");

        await Assert.That(await Render(endpoint, "")).Contains("""
            "storageNamespace":"orders"
            """);
    }

    /// <summary>
    /// A root-relative endpoint names a route of the app, the way MapGraphQL("/graphql") does, so it
    /// is under the app's path base — an IIS virtual directory or UsePathBase — and not at the
    /// origin's root.
    /// </summary>
    [Test]
    public async Task ARootRelativeEndpointIsUnderThePathBase()
    {
        var options = new BlazorQLIdeOptions
        {
            SubscriptionEndpoint = "/graphql/ws"
        };
        var endpoint = new IdeEndpoint(options, "/blazorql", "Orders");

        var page = await Render(endpoint, "/app");

        await Assert.That(page).Contains("""
            "endpoint":"/app/graphql","subscriptionEndpoint":"/app/graphql/ws"
            """);
    }

    [Test]
    public async Task WithoutAPathBaseTheEndpointIsAsWritten() =>
        await Assert.That(await Render(new(new(), "/blazorql", "Orders"), "")).Contains("""
            "endpoint":"/graphql"
            """);

    /// <summary>An absolute url, protocol-relative included, names a host of its own.</summary>
    [Test]
    [Arguments("https://api.example.com/graphql")]
    [Arguments("wss://api.example.com/graphql")]
    [Arguments("//api.example.com/graphql")]
    public async Task AnAbsoluteEndpointIsAsWritten(string url)
    {
        var options = new BlazorQLIdeOptions
        {
            Endpoint = url
        };

        await Assert.That(await Render(new(options, "/blazorql", "Orders"), "/app")).Contains($"""
            "endpoint":"{url}"
            """);
    }

    /// <summary>
    /// Behind a proxy that strips a prefix without saying so, the request's path base is empty; the
    /// override is what knows the public path, and the app's is the override less the mount.
    /// </summary>
    [Test]
    public async Task ARootRelativeEndpointIsUnderTheBasePathOverride()
    {
        var options = new BlazorQLIdeOptions
        {
            BasePathOverride = "/api/blazorql/"
        };

        await Assert.That(await Render(new(options, "/blazorql", "Orders"), "")).Contains("""
            "endpoint":"/api/graphql"
            """);
    }

    /// <summary>
    /// An override that does not end in the mount says nothing about where the app is, so the
    /// endpoint stays under the request's path base.
    /// </summary>
    [Test]
    public async Task AnOverrideNotEndingInTheMountLeavesThePathBase()
    {
        var options = new BlazorQLIdeOptions
        {
            BasePathOverride = "/ide/"
        };

        await Assert.That(await Render(new(options, "/blazorql", "Orders"), "/app")).Contains("""
            "endpoint":"/app/graphql"
            """);
    }

    /// <summary>
    /// A mount serving every extensionless path under it still has the one namespace: deriving from
    /// the request path would move a session's storage whenever the user typed a different url.
    /// </summary>
    [Test]
    public async Task UnknownPathsUnderAMountShareItsNamespace()
    {
        var options = new BlazorQLIdeOptions
        {
            MapUnknownPathsToIde = true
        };
        var endpoint = new IdeEndpoint(options, "/blazorql", "Orders");

        var context = new DefaultHttpContext();
        context.Request.RouteValues["path"] = "some/deep/link";
        var body = new MemoryStream();
        context.Response.Body = body;

        await endpoint.Handle(context);

        await Assert.That(Encoding.UTF8.GetString(body.ToArray())).Contains("""
            "storageNamespace":"blazorql/Orders/blazorql"
            """);
    }
}