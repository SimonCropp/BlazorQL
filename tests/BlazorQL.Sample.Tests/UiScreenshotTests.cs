/// <summary>
/// Verify.Playwright captures of the sample at a fixed viewport. These pngs are the images the
/// docs embed — an <c>&lt;img&gt;</c> in readme/docs points straight at a <c>*.verified.png</c>, so
/// a published screenshot cannot drift from the UI: a change fails the snapshot, and accepting the
/// new baseline is what republishes the image.
/// </summary>
[TestFixture]
[Category("Browser")]
public class UiScreenshotTests :
    BrowserFixture
{
    static readonly ViewportSize viewport = new()
    {
        Width = 1400,
        Height = 900
    };

    [Test]
    public async Task HeroLight()
    {
        var page = await OpenWithQueryRun("light");
        await Verify(page)
            .PageScreenshotOptions(new(), screenshotOnly: true);
    }

    [Test]
    public async Task HeroDark()
    {
        var page = await OpenWithQueryRun("dark");
        await Verify(page)
            .PageScreenshotOptions(new(), screenshotOnly: true);
    }

    [Test]
    public async Task DocExplorer()
    {
        var page = await NewSizedPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await ForceTheme(page, "light");

        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync("[data-testid='doc-search']", 10);
        // The Test type page, deprecated fields revealed — the densest page the explorer renders.
        await page.ClickAsync(".blazorql-type-link:text-is('Test')");
        await page.WaitForSelectorAsync("[data-testid='doc-type']", 10);
        var toggle = page.Locator("button:has-text('Show Deprecated Fields')");
        if (await toggle.CountAsync() > 0)
        {
            await toggle.ClickAsync();
        }

        await Verify(page)
            .PageScreenshotOptions(new(), screenshotOnly: true);
    }

    [Test]
    public async Task History()
    {
        var page = await NewSizedPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await ForceTheme(page, "light");

        await RunAsync(page, "query People { person { name } }");
        await RunAsync(page, "query Flags { isTest }");
        await page.ClickAsync("[data-testid='sidebar-history']");
        await page.WaitForSelectorAsync("[data-testid='history-item']", 10);
        await PinStatusLine(page);

        await Verify(page)
            .PageScreenshotOptions(new(), screenshotOnly: true);
    }

    [Test]
    public async Task SettingsDialog()
    {
        var page = await NewSizedPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await ForceTheme(page, "light");

        await page.ClickAsync("[data-testid='settings']");
        await page.WaitForSelectorAsync("[data-testid='settings-dialog']", 10);

        await Verify(page)
            .PageScreenshotOptions(new(), screenshotOnly: true);
    }

    async Task<IPage> OpenWithQueryRun(string theme)
    {
        var page = await NewSizedPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await ForceTheme(page, theme);

        await RunAsync(
            page,
            """
            query Example {
              person {
                name
                age
              }
              isTest
              longDescriptionType {
                id
              }
            }
            """);
        await PinStatusLine(page);
        return page;
    }

    static async Task RunAsync(IPage page, string query)
    {
        await page.SetEditorValueAsync(query);
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().length > 2)",
            null,
            new() {Timeout = 30_000});
    }

    // The elapsed milliseconds differ run to run; pin them so the capture is of the layout.
    static Task PinStatusLine(IPage page) =>
        page.EvaluateAsync(
            """
            () => {
                const line = document.querySelector('[data-testid=status-line]');
                if (line) {
                    line.textContent = line.textContent.replace(/\d+ ms/, '12 ms');
                }
            }
            """);

    // The docs images must not depend on the machine's color-scheme preference.
    static async Task ForceTheme(IPage page, string theme)
    {
        await page.EvaluateAsync("t => document.documentElement.dataset.theme = t", theme);
        await page.EvaluateAsync("t => monaco.editor.setTheme(t === 'dark' ? 'vs-dark' : 'vs')", theme);
    }

    Task<IPage> NewSizedPageAsync() =>
        NewPageAsync(new() {ViewportSize = viewport});
}
