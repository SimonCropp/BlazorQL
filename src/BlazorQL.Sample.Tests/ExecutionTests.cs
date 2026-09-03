/// <summary>
/// The core loop against the in-browser schema: schema-aware completion and validation from the
/// language worker, execution through the local GraphQL.NET fetcher, and subscription streaming —
/// all with no server anywhere.
/// </summary>
[TestFixture]
[Category("Browser")]
public class ExecutionTests :
    BrowserFixture
{
    [Test]
    public async Task CompletionOffersSchemaFields()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ ");
        var suggestions = await page.SuggestAsync();

        Assert.That(suggestions, Does.Contain("test").And.Contain("person").And.Contain("hasArgs"));
    }

    [Test]
    public async Task ValidationFlagsAnUnknownField()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ nope }");
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModelMarkers({})
                    .some(_ => _.message.includes('Cannot query field'))
            """,
            null,
            new() {Timeout = 30_000});
    }

    [Test]
    public async Task ExecutesAQuery()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("{ id isTest image }");
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

    [Test]
    public async Task StreamsASubscription()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync("subscription { message(delay: 50) }");
        await page.ClickAsync("[data-testid='execute']");

        // The last of the five greetings; each event replaced the response on its way through.
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => _.uri.path.includes('response') &&
                               _.getValue().includes('Zdravo'))
            """,
            null,
            new() {Timeout = 30_000});
    }

    [Test]
    public async Task ResolvesDeferrableFields()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        // GraphQL.NET has no incremental delivery, so no @defer here: both fields arrive in the
        // one response document, the slow one after its resolver's delay.
        await page.SetEditorValueAsync("query { deferrable { normalString deferredString(delay: 100) } }");
        await page.ClickAsync("[data-testid='execute']");

        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModels()
                    .some(_ => {
                        if (!_.uri.path.includes('response')) {
                            return false;
                        }
                        const text = _.getValue();
                        return text.includes('Nice') && text.includes('longer than I thought');
                    })
            """,
            null,
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>
    /// The variables pane describes the operation that will run, not whichever one happens to be
    /// written first. With several in the document that is the picker's (or the caret's) choice.
    /// </summary>
    [Test]
    public async Task VariablesAreCheckedAgainstThePickedOperation()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        await page.SetEditorValueAsync(
            """
            query A($a: Int) { hasArgs(int: $a) }
            query B($b: String) { hasArgs(string: $b) }
            """);
        await page.SetModelValueAsync("variables", """{"b": "text"}""");

        // A declares no $b, and A is what an untouched document runs.
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor
                    .getModelMarkers({owner: 'blazorql-variables'})
                    .some(_ => _.message.includes('$b is not declared'))
            """,
            null,
            new() {Timeout = 30_000});

        await page.ClickAsync("[data-testid='execute']");
        await page.ClickAsync("[data-testid='operation-picker'] >> text=B");

        // B declares it, so the pane has nothing left to say.
        await page.WaitForFunctionAsync(
            """
            () => monaco.editor.getModelMarkers({owner: 'blazorql-variables'}).length === 0
            """,
            null,
            new() {Timeout = 30_000});
    }
}
