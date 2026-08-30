using System.Text.Json;

/// <summary>
/// The M5 documentation explorer over the published sample: stack navigation from the root page
/// through type and field pages, deprecated-member toggles, search, the SDL view, and
/// ctrl-click jump-to-doc from the operation editor.
/// </summary>
[TestFixture]
[Category("Browser")]
public class DocExplorerUiTests :
    BrowserFixture
{
    static async Task OpenDocsAsync(IPage page)
    {
        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync("[data-testid='doc-root']", 30);
    }

    [Test]
    public async Task NavigatesFromRootThroughTypeAndFieldPages()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await OpenDocsAsync(page);

        // The root page shows the root types with the query type linked.
        await page.WaitForSelectorAsync(".blazorql-doc-page:has-text('Root Types')", 10);
        await page.ClickAsync(".blazorql-doc-root-type .blazorql-type-link:text-is('Test')");

        // The type page lists fields, with deprecated ones behind the toggle.
        await page.WaitForSelectorAsync("[data-testid='doc-type']", 10);
        await page.WaitForSelectorAsync(".blazorql-field-link:text-is('hasArgs')", 10);
        Assert.That(await page.Locator(".blazorql-field-link:text-is('deprecatedField')").CountAsync(), Is.Zero);

        await page.ClickAsync("button:has-text('Show Deprecated Fields')");
        await page.WaitForSelectorAsync(".blazorql-field-link:text-is('deprecatedField')", 10);

        // A field page shows the return type and the arguments.
        await page.ClickAsync(".blazorql-field-link:text-is('hasArgs')");
        await page.WaitForSelectorAsync("[data-testid='doc-field']", 10);
        await page.WaitForSelectorAsync("[data-testid='doc-field']:has-text('Type')", 10);
        await page.WaitForSelectorAsync("[data-testid='doc-field']:has-text('Arguments')", 10);

        // And back walks up to the type page.
        await page.ClickAsync("[data-testid='doc-back']");
        await page.WaitForSelectorAsync("[data-testid='doc-type']", 10);

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task SearchFindsTheField()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await OpenDocsAsync(page);

        await page.FillAsync("[data-testid='doc-search'] input", "hasArgs");
        await page.WaitForSelectorAsync(".blazorql-doc-search-result:has-text('Test.hasArgs')", 10);

        // Selecting the result opens the field page.
        await page.ClickAsync(".blazorql-doc-search-result:text-is('Test.hasArgs')");
        await page.WaitForSelectorAsync("[data-testid='doc-field']", 10);

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task SdlToggleShowsTheSchemaText()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await OpenDocsAsync(page);

        await page.ClickAsync("[data-testid='doc-sdl']");

        // The lazily created read-only editor carries the printed schema.
        await page.WaitForSelectorAsync(".blazorql-docs-sdl:not(.blazorql-hidden)", 10);
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('schema.graphql') &&
                               _.getValue().includes('type Test'))
            """,
            null,
            new() {Timeout = 30_000});

        // Toggling back returns to the navigation view without disposing the editor.
        await page.ClickAsync("[data-testid='doc-sdl']");
        await page.WaitForSelectorAsync("[data-testid='doc-root']", 10);

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task CtrlClickOnAFieldJumpsToItsDocumentation()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query { person { name } }");

        // The pixel position of the middle of 'person' (line 1, column 10).
        var position = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const editor = monaco.editor.getEditors()[0];
                const visible = editor.getScrolledVisiblePosition({ lineNumber: 1, column: 10 });
                const rect = editor.getDomNode().getBoundingClientRect();
                return { x: rect.left + visible.left, y: rect.top + visible.top + visible.height / 2 };
            }
            """);

        await page.Keyboard.DownAsync("Control");
        await page.Mouse.ClickAsync(
            (float) position.GetProperty("x").GetDouble(),
            (float) position.GetProperty("y").GetDouble());
        await page.Keyboard.UpAsync("Control");

        // The docs pane opens on the field page, stacked on its parent type.
        await page.WaitForSelectorAsync("[data-testid='doc-field']", 30);
        await page.WaitForSelectorAsync(".blazorql-doc-title:text-is('person')", 10);
        await page.WaitForSelectorAsync("[data-testid='doc-back'][aria-label='Go back to Test']", 10);

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
