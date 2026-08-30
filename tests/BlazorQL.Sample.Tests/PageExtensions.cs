/// <summary>Playwright conveniences for the browser tests.</summary>
static class PageExtensions
{
    /// <summary>
    /// Waits for <paramref name="selector"/> to appear, timing out after <paramref name="seconds"/>
    /// seconds — a terser overload of Playwright's millisecond options-object form.
    /// </summary>
    public static Task<IElementHandle?> WaitForSelectorAsync(this IPage page, string selector, int seconds) =>
        page.WaitForSelectorAsync(
            selector,
            new()
            {
                Timeout = seconds * 1000
            });

    /// <summary>
    /// Opens the app and waits until it is usable: Monaco mounted and the root component's
    /// data-ready marker set. Downloading and booting the WASM runtime is slow on a cold load,
    /// hence the long wait.
    /// </summary>
    public static async Task GoToAppAsync(this IPage page, string baseUrl)
    {
        await page.GotoAsync(baseUrl);
        await page.WaitForSelectorAsync(".monaco-editor", 60);
        await page.WaitForSelectorAsync("[data-testid='blazorql'][data-ready]", 90);
    }

    /// <summary>
    /// Sets the operation editor's content and leaves the caret at the end of it, which is where a
    /// user who typed the query would have left it.
    /// </summary>
    public static Task SetEditorValueAsync(this IPage page, string query) =>
        page.EvaluateAsync(
            """
            query => {
                const editor = monaco.editor.getEditors()[0];
                editor.setValue(query);
                const model = editor.getModel();
                const line = model.getLineCount();
                editor.setPosition({ lineNumber: line, column: model.getLineMaxColumn(line) });
            }
            """,
            query);

    /// <summary>
    /// Sets the content of the Monaco model whose uri contains <paramref name="uriPart"/> —
    /// how the tests reach the variables and headers editors, visible or not.
    /// </summary>
    public static Task SetModelValueAsync(this IPage page, string uriPart, string text) =>
        page.EvaluateAsync(
            """
            args => monaco.editor.getModels()
                .find(m => m.uri.path.includes(args.uriPart))
                .setValue(args.text)
            """,
            new
            {
                uriPart,
                text
            });

    /// <summary>Reads the content of the Monaco model whose uri contains <paramref name="uriPart"/>.</summary>
    public static Task<string> GetModelValueAsync(this IPage page, string uriPart) =>
        page.EvaluateAsync<string>(
            """
            uriPart => monaco.editor.getModels()
                .find(m => m.uri.path.includes(uriPart))
                .getValue()
            """,
            uriPart);

    /// <summary>
    /// Opens Monaco's completion dropdown at the caret and returns the labels it is showing, in
    /// order. The first call on a page pays for the language worker's cold start, hence the long
    /// default.
    /// </summary>
    public static async Task<IReadOnlyList<string>> SuggestAsync(this IPage page, int seconds = 60)
    {
        await page.EvaluateAsync(
            """
            () => {
                const editor = monaco.editor.getEditors()[0];
                editor.focus();
                editor.trigger('test', 'editor.action.triggerSuggest', {});
            }
            """);

        await page.WaitForSelectorAsync(".suggest-widget .monaco-list-row", seconds);
        return await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('.suggest-widget .monaco-list-row'))
                .map(row => row.querySelector('.label-name'))
                .filter(label => label)
                .map(label => label.textContent.trim())
            """);
    }
}
