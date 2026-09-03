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
        var context = new DefaultHttpContext();
        context.Request.PathBase = pathBase;
        var body = new MemoryStream();
        context.Response.Body = body;

        await endpoint.WriteIndex(context);

        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Test]
    public async Task ThePageCarriesTheRequestsBaseHref()
    {
        var endpoint = new IdeEndpoint(new(), "/graphql-ide");

        Assert.That(await Render(endpoint, "/one"), Does.Contain("""<base href="/one/graphql-ide/" />"""));
        Assert.That(await Render(endpoint, "/two"), Does.Contain("""<base href="/two/graphql-ide/" />"""));
        Assert.That(endpoint.CachedPages, Is.EqualTo(2));
    }

    /// <summary>Renders are cached, so the same base path does not pay twice.</summary>
    [Test]
    public async Task ARepeatedBasePathAddsNothing()
    {
        var endpoint = new IdeEndpoint(new(), "/graphql-ide");

        await Render(endpoint, "/same");
        await Render(endpoint, "/same");

        Assert.That(endpoint.CachedPages, Is.EqualTo(1));
    }

    [Test]
    public async Task ThousandsOfBasePathsDoNotGrowTheCacheWithoutLimit()
    {
        var endpoint = new IdeEndpoint(new(), "/graphql-ide");

        for (var index = 0; index < 2000; index++)
        {
            await Render(endpoint, $"/prefix{index}");
        }

        Assert.That(endpoint.CachedPages, Is.LessThanOrEqualTo(32));

        // And every one of them still got its own base href, cached or not.
        Assert.That(await Render(endpoint, "/prefix1999"), Does.Contain("""<base href="/prefix1999/graphql-ide/" />"""));
    }
}
