/// <summary>
/// The rendered index cache, exercised without a server. Behind UseForwardedHeaders honouring
/// X-Forwarded-Prefix from an untrusted hop, the base href a request resolves to is the client's to
/// choose, so the map it keys has to be bounded.
/// </summary>
[TestFixture]
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

        Assert.That(await Render(endpoint, "/one"), Does.Contain("""<base href="/one/blazorql/" />"""));
        Assert.That(await Render(endpoint, "/two"), Does.Contain("""<base href="/two/blazorql/" />"""));
        Assert.That(endpoint.CachedPages, Is.EqualTo(2));
    }

    /// <summary>Renders are cached, so the same base path does not pay twice.</summary>
    [Test]
    public async Task ARepeatedBasePathAddsNothing()
    {
        var endpoint = new IdeEndpoint(new(), "/blazorql", "Orders");

        await Render(endpoint, "/same");
        await Render(endpoint, "/same");

        Assert.That(endpoint.CachedPages, Is.EqualTo(1));
    }

    [Test]
    public async Task ThousandsOfBasePathsDoNotGrowTheCacheWithoutLimit()
    {
        var endpoint = new IdeEndpoint(new(), "/blazorql", "Orders");

        for (var index = 0; index < 2000; index++)
        {
            await Render(endpoint, $"/prefix{index}");
        }

        Assert.That(endpoint.CachedPages, Is.LessThanOrEqualTo(32));

        // And every one of them still got its own base href, cached or not.
        Assert.That(await Render(endpoint, "/prefix1999"), Does.Contain("""<base href="/prefix1999/blazorql/" />"""));
    }

    /// <summary>
    /// localStorage is scoped to an origin and not to a path, so what separates two IDEs a host has
    /// served is the app that mounted each one, and then the mount within that app.
    /// </summary>
    [TestCase("Orders", "", "blazorql/Orders")]
    [TestCase("Orders", "/blazorql", "blazorql/Orders/blazorql")]
    [TestCase("Orders", "/admin/graphql", "blazorql/Orders/admin/graphql")]
    // Two apps that both take the default mount, the case the mount alone cannot separate.
    [TestCase("Billing", "/blazorql", "blazorql/Billing/blazorql")]
    // No entry assembly to name the app after. The mount still separates what it can.
    [TestCase("", "/blazorql", "blazorql/blazorql")]
    public async Task TheAppAndMountNamespaceStorage(string application, string prefix, string expected)
    {
        var endpoint = new IdeEndpoint(new(), prefix, application);

        Assert.That(await Render(endpoint, ""), Does.Contain($"""
            "storageNamespace":"{expected}"
            """));
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

        Assert.That(await Render(endpoint, ""), Does.Contain("""
            "storageNamespace":"orders"
            """));
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

        Assert.That(Encoding.UTF8.GetString(body.ToArray()), Does.Contain("""
            "storageNamespace":"blazorql/Orders/blazorql"
            """));
    }
}
