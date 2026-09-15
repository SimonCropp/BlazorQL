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

    /// <summary>
    /// The version sits beside the logo in the session header, so a consumer looking at a deployed
    /// IDE can say which BlazorQL it is without opening the package manifest.
    /// </summary>
    [Test]
    public async Task TheHeaderShowsTheVersionBesideTheLogo()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        var version = page.Locator("[data-testid='blazorql-version']");
        var text = await version.InnerTextAsync();

        Assert.That(text, Does.Match(@"^v\d+\.\d+\.\d+"));
        // Beside the logo, not somewhere else in the header.
        Assert.That(
            await page.Locator(".blazorql-logo [data-testid='blazorql-version']").CountAsync(),
            Is.EqualTo(1));
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

    /// <summary>
    /// Waits until the editor models satisfy the condition, in which <c>text('variables')</c> reads the
    /// model whose uri contains that part. LoadActiveTab writes the editors one at a time, so a wait on
    /// a tab's content names every model it depends on.
    /// </summary>
    static Task WaitForModelsAsync(IPage page, string condition) =>
        page.WaitForFunctionAsync(
            $$"""
            () => {
                const text = part => monaco.editor
                        .getModels()
                        .find(_ => _.uri.path.includes(part))
                        .getValue();
                return {{condition}};
            }
            """,
            null,
            new() {Timeout = 30_000});

    /// <summary>
    /// Waits until the persisted tab state satisfies the condition, in which <c>tabs</c> is the stored
    /// array. An editor's debounced write persists as it lands, so this is also how a test knows an
    /// edit has reached its tab.
    /// </summary>
    static Task WaitForStoredTabsAsync(IPage page, string condition) =>
        page.WaitForFunctionAsync(
            $$"""
            () => {
                const tabs = JSON.parse(localStorage.getItem('blazorql:tabState') ?? '{}').tabs ?? [];
                return {{condition}};
            }
            """,
            null,
            new() {Timeout = 30_000});

    /// <summary>
    /// Duplicating copies the active tab into a new tab directly to its right — not at the end of the
    /// strip — and makes the copy active. The copy carries the query, the variables and the response,
    /// and is a tab of its own from then on: editing it leaves the source alone.
    /// </summary>
    [Test]
    public async Task DuplicatingATabInsertsAnIndependentCopyBesideIt()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query Source($x: Int) { id hasArgs(int: $x) }");

        // A second tab, so a copy appended to the end of the strip would be in the wrong place. It
        // runs, to leave a status line for the switch back to clear.
        await page.ClickAsync("[data-testid='tab-add']");
        await WaitForOperationTextAsync(page, "_.getValue() === ''");
        await page.SetEditorValueAsync("query Other { isTest }");
        await page.ClickAsync("[data-testid='execute']");
        await page.WaitForSelectorAsync("[data-testid='status-line']", 10);

        // LoadActiveTab clears the status line after its last editor write, so the line going is the
        // sign the tab has loaded.
        await page.ClickAsync(".blazorql-tab-button:has-text('Source')");
        await page.WaitForSelectorAsync("[data-testid='status-line']", new() {State = WaitForSelectorState.Detached});

        // The variables editor exists while the tools strip is collapsed.
        await page.SetModelValueAsync("variables", """{"x": 1}""");
        await page.ClickAsync("[data-testid='execute']");
        await WaitForModelsAsync(page, "text('response').includes('abc123')");
        // Seen in storage before the duplicate, so the stored-state check after it starts from
        // settled state rather than state still catching up.
        await WaitForStoredTabsAsync(page, """tabs.length === 2 && tabs[0].variables === '{"x": 1}'""");
        await page.WaitForSelectorAsync("[data-testid='status-line']", 10);

        await page.ClickAsync("[data-testid='tab-duplicate']");
        await page.WaitForSelectorAsync("[data-testid='status-line']", new() {State = WaitForSelectorState.Detached});

        var titles = await page.Locator(".blazorql-tab-button").AllInnerTextsAsync();
        var selected = await page.Locator(".blazorql-tab").Nth(1).GetAttributeAsync("aria-selected");
        var operation = await page.GetModelValueAsync("blazorql-operation");
        var variables = await page.GetModelValueAsync("blazorql-variables");
        var response = await page.GetModelValueAsync("blazorql-response");
        Assert.Multiple(() =>
        {
            // Beside its source rather than after Other, under the same title, and active.
            Assert.That(string.Join(", ", titles), Is.EqualTo("Source, Source, Other"));
            Assert.That(selected, Is.EqualTo("true"));
            // LoadActiveTab has just written all three from the copy, so a response that did not come
            // across would read empty.
            Assert.That(operation, Does.Contain("query Source"));
            Assert.That(variables, Is.EqualTo("""{"x": 1}"""));
            Assert.That(response, Does.Contain("abc123"));
        });

        await page.SetEditorValueAsync("query Copy($x: Int) { id hasArgs(int: $x) }");
        await page.SetModelValueAsync("variables", """{"x": 2}""");

        // The edits land in the copy alone, and the copy persists under an id of its own — a shared
        // one would collide as the strip's render key on the next boot.
        await WaitForStoredTabsAsync(
            page,
            """
            tabs.length === 3 &&
            new Set(tabs.map(_ => _.id)).size === 3 &&
            tabs[0].query.includes('query Source') &&
            tabs[0].variables === '{"x": 1}' &&
            tabs[1].query.includes('query Copy') &&
            tabs[1].variables === '{"x": 2}'
            """);

        await page.ClickAsync(".blazorql-tab-button:has-text('Source')");
        await WaitForModelsAsync(
            page,
            """
            text('operation').includes('query Source') &&
            text('variables') === '{"x": 1}'
            """);

        // The copy's title followed its own query rather than being frozen into a rename.
        Assert.That(
            string.Join(", ", await page.Locator(".blazorql-tab-button").AllInnerTextsAsync()),
            Is.EqualTo("Source, Copy, Other"));
        // A copied id would surface as Blazor's duplicate @key error.
        Assert.That(ConsoleErrors(), Is.Empty);
    }

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

    /// <summary>
    /// Importing a pasted request: the dialog parses as it is filled in, Import opens a tab holding
    /// the query, variables and the headers that survived filtering, and the tab that was already
    /// open survives it.
    /// </summary>
    [Test]
    public async Task ImportingAPastedRequestOpensANewTab()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("query Existing { id }");

        await page.ClickAsync("[data-testid='tab-import']");
        await page.WaitForSelectorAsync("[data-testid='import-dialog']", 10);

        // Filled rather than pasted: a real clipboard paste needs permissions the harness does not
        // grant, and the dialog parses on input either way.
        await page.FillAsync("[data-testid='import-text']", importedCurl);
        await page.WaitForSelectorAsync("[data-testid='import-summary']:has-text('1 variable')", 10);

        await page.ClickAsync("[data-testid='import-confirm']");
        await page.WaitForSelectorAsync(".blazorql-tab.blazorql-active .blazorql-tab-button:has-text('EnableUser')", 10);

        var operation = await page.GetModelValueAsync("blazorql-operation");
        var variables = await page.GetModelValueAsync("blazorql-variables");
        var headers = await page.GetModelValueAsync("blazorql-request-headers");

        Assert.Multiple(() =>
        {
            // The one-line body arrived indented, not as it was pasted.
            Assert.That(operation, Does.Contain("mutation EnableUser("));
            Assert.That(variables, Does.Contain("\"id\""));
            // The custom header survived; the content type and the client hints did not.
            Assert.That(headers, Does.Contain("authorization"));
            Assert.That(headers, Does.Not.Contain("sec-ch-ua"));
        });

        // LoadActiveTab opened the tools strip, because the imported tab has variables.
        Assert.That(await page.Locator(".blazorql-editor-tools.blazorql-collapsed").CountAsync(), Is.Zero);

        var status = await page.WaitForSelectorAsync("[data-testid='status-line']", 10);
        Assert.That(await status!.TextContentAsync(), Is.EqualTo("Imported 1 request · 1 of 3 headers imported"));

        // The tab that was open before the import still holds its own text.
        await page.ClickAsync(".blazorql-tab-button:has-text('Existing')");
        await WaitForOperationTextAsync(page, "_.getValue().includes('Existing')");

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    const string importedCurl =
        """
        curl --url 'https://example.com/graphql' -H 'authorization: Bearer abc' -H 'content-type: application/json' -H 'sec-ch-ua: "Chromium";v="152"' --data-raw '{"operationName":"EnableUser","variables":{"id":"a"},"query":"mutation EnableUser($id:ID!){enableUser(id:$id){success}}"}'
        """;
}
