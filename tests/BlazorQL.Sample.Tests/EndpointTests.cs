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
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().includes('Introspection failed'))",
            null,
            new() {Timeout = 30_000});

        // Cleared, the built-in schema loads and executes again.
        await page.FillAsync("[data-testid='endpoint']", "");
        await page.ClickAsync("[data-testid='endpoint-apply']");
        await page.SetEditorValueAsync("{ id }");
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().includes('abc123'))",
            null,
            new() {Timeout = 30_000});
    }
}
