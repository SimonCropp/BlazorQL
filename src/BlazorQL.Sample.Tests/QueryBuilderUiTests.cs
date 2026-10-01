/// <summary>
/// The query builder over the published sample: clicks in the tree become text in the operation
/// editor, typing in the editor shows in the tree, a click is undone like a keystroke, and a query
/// built without typing a character runs.
/// </summary>
[Category("Browser")]
public class QueryBuilderUiTests :
    BrowserFixture
{
    const string field = "[data-testid='builder-field']";

    [Test]
    public async Task BuildsAndRunsAQueryWithoutTyping()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await page.ClickAsync("[data-testid='tab-add']");
        await page.ClickAsync("[data-testid='sidebar-builder']");

        // A blank tab offers a query to start from.
        await page.ClickAsync($"[data-testid='builder-pending'] {field}[data-name='person']");
        await WaitForOperationAsync(page, "query MyQuery {\n  person {\n    name\n    age\n  }\n}\n");

        // The tab takes the new operation's name, as it would had it been typed.
        await page.WaitForSelectorAsync(".blazorql-tab.blazorql-active:has-text('MyQuery')", 10);

        await page.ClickAsync($"{field}[data-name='friends']");
        await WaitForOperationAsync(page, "query MyQuery {\n  person {\n    name\n    age\n    friends {\n      name\n      age\n    }\n  }\n}\n");

        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') && _.getValue().includes('Patrica'))
            """,
            null,
            new() {Timeout = 30_000});

        await Assert.That(ConsoleErrors()).IsEmpty();
    }

    // The tree is drawn from the editor's text, so typing shows up in it.
    [Test]
    public async Task FollowsTheOperationEditor()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await page.ClickAsync("[data-testid='sidebar-builder']");

        await page.SetEditorValueAsync("query Typed { isTest }");

        await page.WaitForSelectorAsync($"{field}[data-name='isTest'][aria-checked='true']", 10);
        // Blazor writes an input's value as the property, which an attribute selector cannot see.
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='builder-operation-name']\")?.value === 'Typed'",
            null,
            new() {Timeout = 10_000});
    }

    // A click is one step of the editor's undo, so the obvious way back from a wrong click works.
    [Test]
    public async Task UndoTakesBackAClick()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await page.SetEditorValueAsync("query Q {\n  isTest\n}\n");
        await page.ClickAsync("[data-testid='sidebar-builder']");
        await WaitForTreeAsync(page, "Q");

        await page.ClickAsync($"[data-testid='builder-operation'] > .blazorql-builder-fields > .blazorql-builder-node > {field}[data-name='id']");
        await WaitForOperationAsync(page, "query Q {\n  id\n  isTest\n}\n");

        await page.EvaluateAsync(
            """
            () => {
                const editor = monaco.editor.getEditors()[0];
                editor.focus();
                editor.trigger('test', 'undo', null);
            }
            """);

        await WaitForOperationAsync(page, "query Q {\n  isTest\n}\n");
        await page.WaitForSelectorAsync($"{field}[data-name='id'][aria-checked='false']", 10);
    }

    [Test]
    public async Task WritesAnArgumentValue()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);
        await page.SetEditorValueAsync("query Q {\n  person {\n    age\n  }\n}\n");
        await page.ClickAsync("[data-testid='sidebar-builder']");
        await WaitForTreeAsync(page, "Q");

        await page.ClickAsync("[data-testid='builder-argument'][data-name='delay']");
        await WaitForOperationAsync(page, "query Q {\n  person {\n    age(delay: 0)\n  }\n}\n");

        await page.FillAsync("input[data-testid='builder-value'][aria-label='delay']", "250");
        await page.PressAsync("input[data-testid='builder-value'][aria-label='delay']", "Enter");
        await WaitForOperationAsync(page, "query Q {\n  person {\n    age(delay: 250)\n  }\n}\n");

        // Letters in an Int are refused rather than written.
        await page.FillAsync("input[data-testid='builder-value'][aria-label='delay']", "soon");
        await page.PressAsync("input[data-testid='builder-value'][aria-label='delay']", "Enter");
        await page.WaitForSelectorAsync("input[data-testid='builder-value'][aria-invalid='true']", 10);
        await Assert.That(await OperationAsync(page)).IsEqualTo("query Q {\n  person {\n    age(delay: 250)\n  }\n}\n");
    }

    /// <summary>
    /// Waits for the tree to be drawn from the operation of that name. The pane follows the tab's
    /// text, which trails the editor by its change debounce, so a click straight after setting the
    /// editor would land on a row of the document before.
    /// </summary>
    static Task WaitForTreeAsync(IPage page, string operation) =>
        page.WaitForFunctionAsync(
            "name => document.querySelector(\"[data-testid='builder-operation-name']\")?.value === name",
            operation,
            new() {Timeout = 10_000});

    // Monaco keeps the model's own line ends, which on Windows are CRLF; the tests compare the text
    // as the builder writes it.
    static async Task<string> OperationAsync(IPage page) =>
        (await page.GetModelValueAsync("blazorql-operation")).Replace("\r\n", "\n");

    static Task WaitForOperationAsync(IPage page, string expected) =>
        page.WaitForFunctionAsync(
            """
            expected => monaco.editor
                    .getModels()
                    .find(_ => _.uri.path.includes('blazorql-operation'))
                    .getValue()
                    .replace(/\r\n/g, '\n') === expected
            """,
            expected,
            new() {Timeout = 10_000});
}
