/// <summary>
/// The M7 toolbar operations over the published sample: prettify, merge, copy, fill-leaves on
/// execute, share links, the response copy/download overlay, the status footer, and the global
/// re-fetch shortcut.
/// </summary>
[TestFixture]
[Category("Browser")]
public class ToolbarTests :
    BrowserFixture
{
    static Task WaitForOperationTextAsync(IPage page, string contains) =>
        page.WaitForFunctionAsync(
            $"""
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('operation') &&
                               _.getValue().includes('{contains}'))
            """,
            null,
            new() {Timeout = 30_000});

    static Task WaitForResponseTextAsync(IPage page, string contains) =>
        page.WaitForFunctionAsync(
            $"""
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('{contains}'))
            """,
            null,
            new() {Timeout = 30_000});

    [Test]
    public async Task PrettifyFormatsTheOperation()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{id isTest}");
        await page.ClickAsync("[data-testid='prettify']");

        // The formatter splits the selection set one indented field per line.
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => {
                        if (!_.uri.path.includes('operation')) {
                            return false;
                        }
                        const text = _.getValue();
                        return text.includes('  id') && text.includes('  isTest') && text.split('\n').length >= 4;
                    })
            """,
            null,
            new() {Timeout = 30_000});
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task MergeInlinesAFragment()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query A { ...F } fragment F on Test { id }");
        await page.ClickAsync("[data-testid='merge']");

        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => {
                        if (!_.uri.path.includes('operation')) {
                            return false;
                        }
                        const text = _.getValue();
                        return text.includes('query A') && text.includes('id') && !text.includes('fragment');
                    })
            """,
            null,
            new() {Timeout = 30_000});
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task CopyButtonDoesNotThrow()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ id }");
        await page.ClickAsync("[data-testid='copy']");

        // The clipboard write is best-effort; the observable contract is a clean console.
        await page.WaitForTimeoutAsync(500);
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task FillLeavesCompletesABareObjectField()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // person is an object type: the run first fills in its default leaf fields, then executes
        // the filled document.
        await page.SetEditorValueAsync("{ person }");
        await page.ClickAsync("[data-testid='execute']");

        await WaitForOperationTextAsync(page, "person {");
        await WaitForResponseTextAsync(page, "Mark");
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task ShareLinkRoundTripsThroughTheHash()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query Shared { isTest }");
        await page.ClickAsync("[data-testid='share']");

        await page.WaitForFunctionAsync(
            "() => location.hash.startsWith('#q=')",
            null,
            new() {Timeout = 10_000});

        // Reloading the share url restores the query — the hash wins over storage.
        var shareUrl = await page.EvaluateAsync<string>("() => location.href");
        var second = await NewPageAsync();
        await second.GotoAsync(shareUrl);
        await second.WaitForSelectorAsync(".monaco-editor", 60);
        await second.WaitForSelectorAsync("[data-testid='blazorql'][data-ready]", 90);
        await WaitForOperationTextAsync(second, "query Shared");

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task ResponseActionsAppearAndDownloadFires()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ id }");
        await page.ClickAsync("[data-testid='execute']");
        await WaitForResponseTextAsync(page, "abc123");

        await page.WaitForSelectorAsync("[data-testid='response-copy']", 10);
        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.ClickAsync("[data-testid='response-download']"));

        Assert.That(download.SuggestedFilename, Is.EqualTo("response.json"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task StatusLineReportsASuccessfulRun()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ id }");
        await page.ClickAsync("[data-testid='execute']");
        await WaitForResponseTextAsync(page, "abc123");

        var status = await page.WaitForSelectorAsync("[data-testid='status-line']", 10);
        Assert.That(await status!.TextContentAsync(), Does.StartWith("OK ·").And.EndWith("ms"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task RefetchShortcutStaysClean()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.Keyboard.PressAsync("Shift+Control+R");
        // The re-fetch spins the sidebar icon and completes without console noise.
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-testid=refetch]').disabled",
            null,
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
