using System.Collections.Concurrent;

/// <summary>
/// An endpoint that has not implemented the spec drafts' introspection additions — GraphQL.NET by
/// default — rejects the IDE's first introspection whole, and the IDE retries with the portable
/// query. That is one failed request in the server's logs per load, so the IDE remembers the
/// endpoint and asks it only the portable query from then on.
/// </summary>
[Category("Browser")]
public class PortableIntrospectionTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    /// <summary>Answers any document asking for a draft addition the way GraphQL.NET does.</summary>
    protected override void MapSchema(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/graphql" &&
                HttpMethods.IsPost(context.Request.Method))
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
                if (body.Contains("isRepeatable", StringComparison.Ordinal))
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        """{"errors":[{"message":"Cannot query field 'isRepeatable' on type '__Directive'."}]}""");
                    return;
                }
            }

            await next();
        });
        app.MapSampleSchema();
    }

    [Test]
    public async Task RemembersAnEndpointThatRejectsTheDraftAdditions()
    {
        var page = await NewPageAsync();
        var introspections = new ConcurrentQueue<bool>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" &&
                request.PostData is { } data &&
                data.Contains("IntrospectionQuery", StringComparison.Ordinal))
            {
                introspections.Enqueue(data.Contains("isRepeatable", StringComparison.Ordinal));
            }
        };

        await page.GotoAsync(IdeUrl + "/");
        await page.WaitForSelectorAsync("[data-testid='blazorql'][data-ready]", new() {Timeout = 90_000});

        // First load: the draft query, rejected, then the portable retry.
        await Assert.That(string.Join(",", introspections)).IsEqualTo("True,False");

        introspections.Clear();
        await page.ReloadAsync();
        await page.WaitForSelectorAsync("[data-testid='blazorql'][data-ready]", new() {Timeout = 90_000});

        // Second load: straight to the portable query.
        await Assert.That(string.Join(",", introspections)).IsEqualTo("False");

        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync(
            "[data-testid='plugin-pane']:has-text('TestEnum')",
            new() {Timeout = 30_000});
    }
}
