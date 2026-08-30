/// <summary>
/// The M6 storage layer over the published sample: history recording and restoring, tab/query
/// survival across a reload, theme persistence, and the settings dialog's clear-storage.
/// </summary>
[TestFixture]
[Category("Browser")]
public class PersistenceTests :
    BrowserFixture
{
    /// <summary>Waits until the persisted tab state contains the marker — the debounced write flushed.</summary>
    static Task WaitForPersistedAsync(IPage page, string marker) =>
        page.WaitForFunctionAsync(
            $"() => (localStorage.getItem('blazorql:tabState') ?? '').includes('{marker}')",
            null,
            new() {Timeout = 30_000});

    static Task WaitForOperationTextAsync(IPage page, string contains) =>
        page.WaitForFunctionAsync(
            $"() => monaco.editor.getModels().some(m => m.uri.path.includes('operation') && m.getValue().includes('{contains}'))",
            null,
            new() {Timeout = 30_000});

    [Test]
    public async Task ExecutionAppearsInHistoryAndRestores()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query FromHistory { id }");
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForFunctionAsync(
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().includes('abc123'))",
            null,
            new() {Timeout = 30_000});

        // The pane was closed during the run; the item is there when it opens.
        await page.ClickAsync("[data-testid='sidebar-history']");
        await page.WaitForSelectorAsync("[data-testid='history-item']", 10);
        Assert.That(
            await page.Locator("[data-testid='history-item']").First.TextContentAsync(),
            Is.EqualTo("FromHistory"));

        // Editing the operation away and clicking the item brings the query back.
        await page.SetEditorValueAsync("query SomethingElse { test }");
        await WaitForOperationTextAsync(page, "SomethingElse");
        await page.ClickAsync("[data-testid='history-item']");
        await WaitForOperationTextAsync(page, "FromHistory");

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task TabsAndQueryTextSurviveAReload()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query PersistMe { id }");
        // A second, empty tab proves the whole tab set round-trips, not just the editor text.
        await page.ClickAsync("[data-testid='tab-add']");
        // The debounced write must contain the query *and* both tabs before the reload.
        await page.WaitForFunctionAsync(
            """
            () => {
                const state = localStorage.getItem('blazorql:tabState');
                if (!state || !state.includes('PersistMe')) {
                    return false;
                }

                try {
                    return JSON.parse(state).tabs.length === 2;
                } catch {
                    return false;
                }
            }
            """,
            null,
            new() {Timeout = 30_000});

        await page.ReloadAsync();
        await page.GoToAppAsync(BaseUrl);

        // Both tabs are back, the first still titled by its operation.
        Assert.That(await page.Locator(".blazorql-tab").CountAsync(), Is.EqualTo(2));
        await page.ClickAsync(".blazorql-tab-button:has-text('PersistMe')");
        await WaitForOperationTextAsync(page, "PersistMe");

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task ThemeChoiceSurvivesAReload()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // System -> Light -> Dark.
        await page.ClickAsync("[data-testid='theme-toggle']");
        await page.ClickAsync("[data-testid='theme-toggle']");
        await page.WaitForFunctionAsync(
            "() => document.documentElement.dataset.theme === 'dark' && localStorage.getItem('blazorql:theme') === 'dark'",
            null,
            new() {Timeout = 10_000});

        await page.ReloadAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.WaitForFunctionAsync(
            "() => document.documentElement.dataset.theme === 'dark' && document.querySelector('.monaco-editor').classList.contains('vs-dark')",
            null,
            new() {Timeout = 10_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task SettingsClearDataWipesStorage()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query WipeMe { id }");
        await WaitForPersistedAsync(page, "WipeMe");

        await page.ClickAsync("[data-testid='settings']");
        await page.WaitForSelectorAsync("[data-testid='settings-dialog']", 10);
        await page.ClickAsync("[data-testid='clear-storage']");
        await page.WaitForSelectorAsync("[data-testid='clear-storage']:has-text('Cleared data')", 10);

        // Every namespaced key is gone.
        Assert.That(
            await page.EvaluateAsync<int>("() => Object.keys(localStorage).filter(k => k.startsWith('blazorql:')).length"),
            Is.Zero);

        // A reload boots fresh: one default tab carrying the welcome text.
        await page.ReloadAsync();
        await page.GoToAppAsync(BaseUrl);
        await WaitForOperationTextAsync(page, "Welcome to BlazorQL");
        Assert.That(await page.Locator(".blazorql-tab").CountAsync(), Is.EqualTo(1));

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
