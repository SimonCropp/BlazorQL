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
            "() => monaco.editor.getModelMarkers({}).some(m => m.message.includes('Cannot query field'))",
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
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().includes('abc123'))",
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
            "() => monaco.editor.getModels().some(m => m.uri.path.includes('response') && m.getValue().includes('Zdravo'))",
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
            () => monaco.editor.getModels().some(m => {
                if (!m.uri.path.includes('response')) {
                    return false;
                }
                const text = m.getValue();
                return text.includes('Nice') && text.includes('longer than I thought');
            })
            """,
            null,
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
