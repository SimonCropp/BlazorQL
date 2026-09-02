/// <summary>
/// The whole product claim, end to end: one PackageReference and one MapBlazorQL call put a working
/// IDE in front of a real GraphQL endpoint, with nothing deployed alongside the assembly.
/// </summary>
[TestFixture]
[Category("Browser")]
public class BundledIdeTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    /// <summary>
    /// One assertion covering a lot: a wrong content type, a missing embedded asset, a broken base
    /// href and an integrity failure all surface here, because any of them stops monaco or the
    /// runtime from starting and every one of them logs.
    /// </summary>
    [Test]
    public async Task BootsCleanly()
    {
        var page = await OpenIdeAsync();

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        Assert.That(languages, Does.Contain("graphql").And.Contain("json"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>
    /// Browser to http to the host app's own schema and back — the loop the WebAssembly sample
    /// cannot exercise, because its schema runs in the browser.
    /// </summary>
    [Test]
    public async Task RunsAQueryAgainstTheServer()
    {
        var page = await OpenIdeAsync();

        await page.EvaluateAsync(
            """
            () => {
                const editor = monaco.editor.getEditors()[0];
                editor.setValue('{ id isTest }');
            }
            """);
        await page.ClickAsync("[data-testid='execute']");

        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('abc123'))
            """,
            null,
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>Introspection reached the server and the doc explorer rendered what came back.</summary>
    [Test]
    public async Task IntrospectsTheServerSchema()
    {
        var page = await OpenIdeAsync();

        await page.ClickAsync("[data-testid='sidebar-docs']");
        await page.WaitForSelectorAsync("[data-testid='doc-root']", new() {Timeout = 30_000});

        // A type only the server could have told us about.
        await page.WaitForSelectorAsync(
            "[data-testid='plugin-pane']:has-text('TestEnum')",
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>Schema-aware completion, which only works if introspection actually round-tripped.</summary>
    [Test]
    public async Task CompletionOffersServerSchemaFields()
    {
        var page = await OpenIdeAsync();

        await page.EvaluateAsync(
            """
            () => {
                const editor = monaco.editor.getEditors()[0];
                editor.setValue('{ ');
                const model = editor.getModel();
                const line = model.getLineCount();
                editor.setPosition({ lineNumber: line, column: model.getLineMaxColumn(line) });
                editor.focus();
                editor.trigger('test', 'editor.action.triggerSuggest', {});
            }
            """);

        await page.WaitForSelectorAsync(".suggest-widget .monaco-list-row", new() {Timeout = 60_000});
        var suggestions = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('.suggest-widget .monaco-list-row'))
                    .map(_ => _.querySelector('.label-name'))
                    .filter(_ => _)
                    .map(_ => _.textContent.trim())
            """);

        Assert.That(suggestions, Does.Contain("test").And.Contain("person"));
    }
}

/// <summary>
/// The IDE behind a reverse proxy that mounts the whole app under a prefix. Everything hinges on
/// the base href the middleware writes into index.html.
/// </summary>
[TestFixture]
[Category("Browser")]
public class SubPathBundledIdeTests :
    BundledFixture
{
    protected override string PathBase => "/app";

    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task BootsUnderAPathBase()
    {
        var page = await OpenIdeAsync();

        Assert.That(page.Url, Does.Contain("/app/graphql-ide/"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}

/// <summary>The degenerate mount, which breaks naive prefix arithmetic.</summary>
[TestFixture]
[Category("Browser")]
public class RootMountBundledIdeTests :
    BundledFixture
{
    protected override string Mount => "/";

    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task BootsAtTheRoot()
    {
        await OpenIdeAsync();
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}

/// <summary>
/// A consumer with UseResponseCompression turned on globally. Double-encoding is not subtle - the
/// runtime fails to boot - so a browser test is the strongest available form of this assertion.
/// </summary>
[TestFixture]
[Category("Browser")]
public class CompressedBundledIdeTests :
    BundledFixture
{
    protected override bool UseResponseCompression => true;

    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task SurvivesTheHostsOwnResponseCompression()
    {
        await OpenIdeAsync();
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
