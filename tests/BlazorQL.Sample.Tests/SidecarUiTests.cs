/// <summary>
/// The debug sidecar over the published sample: the floating launcher, capture of executed
/// queries, the detail view, the IDE deep link, the keyboard shortcut, and clearing.
/// </summary>
[TestFixture]
[Category("Browser")]
public class SidecarUiTests :
    BrowserFixture
{
    static async Task RunAsync(IPage page, string query)
    {
        await page.SetEditorValueAsync(query);
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().length > 2)",
            null,
            new() {Timeout = 30_000});
    }

    [Test]
    public async Task CapturesExecutedQueries()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await RunAsync(page, "query People { person { name } }");

        await page.ClickAsync("[data-testid='blazorql-sidecar-toggle']");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        // The IDE introspects on load, so the log holds that request plus the executed one.
        var rows = page.Locator("[data-testid='blazorql-sidecar-entries'] li");
        Assert.That(await rows.CountAsync(), Is.GreaterThanOrEqualTo(2));
        var row = page.Locator("[data-testid='blazorql-sidecar-entries'] li", new() {HasTextString = "People"});
        await row.ClickAsync();

        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar-detail']", 10);
        var query = await page.Locator("[data-testid='blazorql-sidecar-query']").InnerTextAsync();
        Assert.That(query, Does.Contain("query People"));
        var response = await page.Locator("[data-testid='blazorql-sidecar-response']").First.InnerTextAsync();
        Assert.That(response, Does.Contain("Mark"));

        // The deep link is a share fragment carrying exactly the captured operation.
        var href = await page.Locator("[data-testid='blazorql-sidecar-ide-link']").GetAttributeAsync("href");
        Assert.That(href, Does.StartWith("#q="));
        Assert.That(DecodeShareFragment(href!), Does.Contain("query People"));

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>
    /// Decodes a <c>#q=</c> share fragment — base64url(UTF8(JSON)) — back to its JSON text. The
    /// test project deliberately references no BlazorQL assembly, so this mirrors the codec
    /// rather than calling it.
    /// </summary>
    static string DecodeShareFragment(string href)
    {
        var payload = href["#q=".Length..]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    [Test]
    public async Task ShortcutTogglesAndClearEmpties()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.Keyboard.PressAsync("Alt+g");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        // The introspection request is already captured, so clear has something to forget.
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar-entries']", 10);
        await page.ClickAsync("[data-testid='blazorql-sidecar-clear']");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar-empty']", 10);

        await page.Keyboard.PressAsync("Alt+g");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-testid=blazorql-sidecar]')",
            null,
            new() {Timeout = 10_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
