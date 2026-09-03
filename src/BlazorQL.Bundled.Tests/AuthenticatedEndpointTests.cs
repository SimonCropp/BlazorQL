/// <summary>
/// The IDE in front of an endpoint that wants an Authorization header — which is most of them.
/// Introspection has to carry the headers editor's contents or the whole language layer stays dark
/// while every query the user runs works, and the bundled package is where that bites hardest: the
/// IDE is served on the API's own origin.
/// </summary>
[TestFixture]
[Category("Browser")]
public class AuthenticatedEndpointTests :
    BundledFixture
{
    const string token = "Bearer test-token";

    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.DefaultHeaders = $$"""{"Authorization": "{{token}}"}""";
    }

    protected override void MapSchema(WebApplication app) =>
        app.MapSampleSchema("/graphql", _ => _.Request.Headers.Authorization == token);

    /// <summary>
    /// The doc explorer only has anything to show if introspection got past the gate. The console
    /// assertion is the other half: the fixture records every response of 400 or worse, so a 401
    /// on the way lands there even when a later attempt succeeds.
    /// </summary>
    [Test]
    public async Task IntrospectsThroughTheHeadersEditorsHeaders()
    {
        var page = await OpenIdeAsync();

        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync("[data-testid='doc-root']", new() {Timeout = 30_000});
        await page.WaitForSelectorAsync(
            "[data-testid='plugin-pane']:has-text('TestEnum')",
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>The gate is real: without the header the same endpoint refuses.</summary>
    [Test]
    public async Task RefusesAnUnauthenticatedRequest()
    {
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BaseUrl + "/graphql",
            new StringContent(
                """{"query":"{ id }"}""",
                Encoding.UTF8,
                "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
