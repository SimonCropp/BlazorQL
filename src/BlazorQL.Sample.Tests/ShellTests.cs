/// <summary>
/// The M4 layout shell: tabs, editor tools, plugin pane toggles, theming, and the variables/headers
/// wiring into execution — all over the published sample.
/// </summary>
[TestFixture]
[Category("Browser")]
public class ShellTests :
    BrowserFixture
{
    [Test]
    public async Task TabsPreserveQueryTextAcrossAddSwitchClose()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query One { id }");

        // A new tab starts empty and becomes active.
        await page.ClickAsync("[data-testid='tab-add']");
        await WaitForOperationTextAsync(page, "_.getValue() === ''");

        await page.SetEditorValueAsync("query Two { test }");

        // Switching back restores the first tab's text.
        await page.ClickAsync(".blazorql-tab-button:has-text('One')");
        await WaitForOperationTextAsync(page, "_.getValue().includes('One')");

        // And forward again restores the second's.
        await page.ClickAsync(".blazorql-tab-button:has-text('Two')");
        await WaitForOperationTextAsync(page, "_.getValue().includes('Two')");

        // Closing the active tab falls back to its neighbour.
        await page.ClickAsync(".blazorql-tab.blazorql-active [aria-label='Close Tab']");
        await WaitForOperationTextAsync(page, "_.getValue().includes('One')");

        // A single remaining tab has no close button.
        Assert.That(await page.Locator("[aria-label='Close Tab']").CountAsync(), Is.Zero);
    }

    /// <summary>Waits until the operation model (bound as <c>_</c>) satisfies the condition.</summary>
    static Task WaitForOperationTextAsync(IPage page, string condition) =>
        page.WaitForFunctionAsync(
            $"""
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('operation') && ({condition}))
            """,
            null,
            new() {Timeout = 30_000});

    [Test]
    public async Task TabTitleDerivesFromNamedOperation()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query MyOperation { id }");

        // The title updates through the debounced change callback.
        await page.WaitForSelectorAsync(".blazorql-tab-button:has-text('MyOperation')", 30);
    }

    [Test]
    public async Task InvalidVariablesJsonShortCircuitsIntoResponse()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ id }");
        await page.SetModelValueAsync("variables", "{oops");
        await page.ClickAsync("[data-testid='execute']");

        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('invalid JSON'))
            """,
            null,
            new() {Timeout = 30_000});
    }

    [Test]
    public async Task WrongTypedVariableGetsAMarker()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query Q($x: Int){ hasArgs(int: $x) }");
        await page.SetModelValueAsync("variables", """{"x": "nope"}""");

        // The language mode regenerates the variables JSON Schema from the operation and the json
        // worker flags the mistyped value on the variables model.
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModelMarkers({})
                    .some(_ => _.resource.path.includes('variables'))
            """,
            null,
            new() {Timeout = 30_000});
    }

    [Test]
    public async Task PluginPaneTogglesFromTheSidebar()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        Assert.That(await page.Locator("[data-testid='plugin-pane']").CountAsync(), Is.Zero);

        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync("[data-testid='plugin-pane']:has-text('Documentation Explorer')", 10);

        // Another plugin replaces the pane's content.
        await page.ClickAsync("[data-testid='sidebar-history']");
        await page.WaitForSelectorAsync("[data-testid='plugin-pane']:has-text('History')", 10);

        // The same button again closes the pane.
        await page.ClickAsync("[data-testid='sidebar-history']");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='plugin-pane']\")",
            null,
            new() {Timeout = 10_000});
    }

    [Test]
    public async Task ThemeToggleFlipsDataThemeAndMonacoTheme()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // Playwright's default color scheme is light, so System resolves to light at boot.
        Assert.That(await page.EvaluateAsync<string>("() => document.documentElement.dataset.theme"), Is.EqualTo("light"));

        // System -> Light: still light.
        await page.ClickAsync("[data-testid='theme-toggle']");
        // Light -> Dark: the attribute flips and Monaco editors pick up the dark theme class.
        await page.ClickAsync("[data-testid='theme-toggle']");
        await page.WaitForFunctionAsync(
            """
            () => document.documentElement.dataset.theme === 'dark' &&
                  document.querySelector('.monaco-editor').classList.contains('vs-dark')
            """,
            null,
            new() {Timeout = 10_000});

        // Dark -> System: back to light, dark class gone.
        await page.ClickAsync("[data-testid='theme-toggle']");
        await page.WaitForFunctionAsync(
            """
            () => document.documentElement.dataset.theme === 'light' &&
                  !document.querySelector('.monaco-editor').classList.contains('vs-dark')
            """,
            null,
            new() {Timeout = 10_000});
    }

    [Test]
    public async Task EditorToolsChevronCollapsesAndExpands()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // The sample sets no default headers or variables, so the tools start collapsed.
        Assert.That(await page.Locator(".blazorql-editor-tools.blazorql-collapsed").CountAsync(), Is.EqualTo(1));

        await page.ClickAsync("[aria-label='Show editor tools']");
        await page.WaitForSelectorAsync(".blazorql-editor-tools:not(.blazorql-collapsed)", 10);

        await page.ClickAsync("[aria-label='Hide editor tools']");
        await page.WaitForSelectorAsync(".blazorql-editor-tools.blazorql-collapsed", 10);
    }

    [Test]
    public async Task ToolTabsSelectTheirEditor()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // Selecting a tool expands the strip with that editor visible.
        await page.ClickAsync("[data-testid='tools-headers']");
        await page.WaitForSelectorAsync("#blazorql-headers-editor .monaco-editor", 10);

        await page.ClickAsync("[data-testid='tools-variables']");
        await page.WaitForSelectorAsync("#blazorql-variables-editor .monaco-editor", 10);
    }

    /// <summary>
    /// A pane drag used to call into .NET on every pointermove, and every one of those re-rendered
    /// the whole IDE. The moves are now coalesced to one call a frame — and the position the drag
    /// ended on, which coalescing must never lose, still lands.
    /// </summary>
    [Test]
    public async Task APaneDragCoalescesItsMovesAndKeepsTheLastOne()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        var scheduled = await page.EvaluateAsync<int>(
            """
            () => {
                const resizer = document.getElementById('blazorql-session-resizer');
                const rect = resizer.parentElement.getBoundingClientRect();

                let frames = 0;
                const raf = window.requestAnimationFrame;
                window.requestAnimationFrame = callback => {
                    frames++;
                    return raf.call(window, callback);
                };

                const send = (type, clientX) => resizer.dispatchEvent(
                    new PointerEvent(type, {bubbles: true, pointerId: 1, clientX, clientY: rect.top + 10}));

                send('pointerdown', rect.left + rect.width * 0.5);
                // Fifty moves in one task, as a real drag delivers between frames.
                for (let step = 0; step < 50; step++) {
                    send('pointermove', rect.left + rect.width * (0.5 - step * 0.004));
                }

                send('pointerup', rect.left + rect.width * 0.3);
                window.requestAnimationFrame = raf;
                return frames;
            }
            """);

        // One frame for fifty moves, not fifty.
        Assert.That(scheduled, Is.GreaterThan(0).And.LessThan(5));

        // The drag ended at 0.3 of the container, and that is where the editors column sits.
        await page.WaitForFunctionAsync(
            """
            () => {
                const column = document.querySelector('.blazorql-editors-column');
                const grow = parseFloat(getComputedStyle(column).flexGrow);
                return Math.abs(grow - 0.3) < 0.02;
            }
            """,
            null,
            new() {Timeout = 10_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
