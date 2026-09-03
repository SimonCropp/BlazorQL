/// <summary>
/// The sample's endpoint box: applying an endpoint swaps the IDE's fetcher, which re-introspects.
/// A dead endpoint fails visibly; clearing it restores the in-browser schema. Console errors are
/// deliberately not asserted here — the dead endpoint logs network failures.
/// </summary>
[TestFixture]
[Category("Browser")]
public class EndpointTests :
    BrowserFixture
{
    [Test]
    public async Task EndpointBoxSwapsFetcher()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // An endpoint nothing listens on: the swap re-introspects and fails into the response pane.
        await page.FillAsync("[data-testid='endpoint']", "http://127.0.0.1:1/graphql");
        await page.ClickAsync("[data-testid='endpoint-apply']");
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('Introspection failed'))
            """,
            null,
            new() {Timeout = 30_000});

        // Cleared, the built-in schema loads and executes again.
        await page.FillAsync("[data-testid='endpoint']", "");
        await page.ClickAsync("[data-testid='endpoint-apply']");
        await page.SetEditorValueAsync("{ id }");
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('abc123'))
            """,
            null,
            new() {Timeout = 30_000});
    }

    /// <summary>
    /// Applying one endpoint then another before the first has answered: the first result must not
    /// land. It described a fetcher that is gone, so installing it would leave the schema, the SDL
    /// and the validator describing an endpoint no request goes to.
    /// </summary>
    [Test]
    public async Task ASlowSchemaThatArrivesLateDoesNotReplaceTheCurrentOne()
    {
        var page = await NewPageAsync();

        // An endpoint that answers, but only after the next one has already been applied. Its
        // schema has one field, which nothing in the built-in schema is called. The completion
        // source is how the test knows the slow answer has been delivered — or refused, which is
        // what a cancelled introspection looks like from here.
        var answered = new TaskCompletionSource();
        await page.RouteAsync(
            "**/slow-graphql",
            async route =>
            {
                await Task.Delay(3000);
                try
                {
                    await route.FulfillAsync(
                        new()
                        {
                            ContentType = "application/json",
                            Body = slowSchema
                        });
                }
                finally
                {
                    answered.TrySetResult();
                }
            });

        await page.GoToAppAsync(BaseUrl);

        await page.FillAsync("[data-testid='endpoint']", $"{BaseUrl}/slow-graphql");
        await page.ClickAsync("[data-testid='endpoint-apply']");

        // Back to the in-browser schema, which answers at once.
        await page.FillAsync("[data-testid='endpoint']", "");
        await page.ClickAsync("[data-testid='endpoint-apply']");
        await page.SetEditorValueAsync("{ ");
        Assert.That(await page.SuggestAsync(), Does.Contain("person"));

        // Now let the slow one land, and give its continuation room to run.
        await answered.Task;
        await page.WaitForTimeoutAsync(1000);

        await page.SetEditorValueAsync("{ ");
        var suggestions = await page.SuggestAsync();

        Assert.That(suggestions, Does.Contain("person"));
        Assert.That(suggestions, Does.Not.Contain("onlyFromSlow"));
    }

    const string slowSchema =
        """
        {"data":{"__schema":{
          "queryType": {"name": "Query"},
          "types": [
            {"kind": "OBJECT", "name": "Query", "fields": [
              {"name": "onlyFromSlow", "args": [], "isDeprecated": false,
               "type": {"kind": "SCALAR", "name": "String"}}
            ]},
            {"kind": "SCALAR", "name": "String"}
          ],
          "directives": []
        }}}
        """;
}
