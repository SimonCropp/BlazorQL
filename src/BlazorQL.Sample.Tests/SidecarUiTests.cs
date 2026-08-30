/// <summary>
/// The debug sidecar over the published sample: the floating launcher, capture of the app's
/// requests, the detail view, the IDE deep link, the keyboard shortcut, and clearing. The sample
/// renders it on its app page only — the query explorer is the IDE itself, so it never shows there.
/// </summary>
[TestFixture]
[Category("Browser")]
public class SidecarUiTests :
    BrowserFixture
{
    [Test]
    public async Task CapturesExecutedQueries()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        // A second request on top of the page's load-time query.
        await page.FillAsync("[data-testid='home-echo-input']", "from the sidecar test");
        await page.ClickAsync("[data-testid='home-echo-send']");
        await page.WaitForSelectorAsync("[data-testid='home-echo-result']", 10);

        await page.ClickAsync("[data-testid='blazorql-sidecar-toggle']");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        var rows = page.Locator("[data-testid='blazorql-sidecar-entries'] li");
        Assert.That(await rows.CountAsync(), Is.GreaterThanOrEqualTo(2));
        var row = page.Locator("[data-testid='blazorql-sidecar-entries'] li", new() {HasTextString = "Echo"});
        await row.ClickAsync();

        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar-detail']", 10);
        var query = await page.Locator("[data-testid='blazorql-sidecar-query']").InnerTextAsync();
        Assert.That(query, Does.Contain("mutation Echo"));
        var response = await page.Locator("[data-testid='blazorql-sidecar-response']").First.InnerTextAsync();
        Assert.That(response, Does.Contain("from the sidecar test"));

        // The deep link routes to the explorer page with a share fragment carrying exactly the
        // captured operation.
        var href = await page.Locator("[data-testid='blazorql-sidecar-ide-link']").GetAttributeAsync("href");
        Assert.That(href, Does.StartWith("explorer#q="));
        Assert.That(DecodeShareFragment(href!), Does.Contain("mutation Echo"));

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>
    /// Decodes a <c>#q=</c> share fragment — base64url(UTF8(JSON)) — back to its JSON text. The
    /// test project deliberately references no BlazorQL assembly, so this mirrors the codec
    /// rather than calling it.
    /// </summary>
    static string DecodeShareFragment(string href)
    {
        var payload = href[(href.IndexOf("#q=", StringComparison.Ordinal) + "#q=".Length)..]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    [Test]
    public async Task ShortcutTogglesAndClearEmpties()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        await page.Keyboard.PressAsync("Alt+g");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        // The page's load-time query is already captured, so clear has something to forget.
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
