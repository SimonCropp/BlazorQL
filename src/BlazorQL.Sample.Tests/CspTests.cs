/// <summary>
/// The policy documented in docs/csp.md, over the RCL rather than the bundled package. Everything
/// it has to widen comes from Blazor WebAssembly and Monaco, so the two deliveries need the same
/// directives, and this fixture is what keeps that claim honest on the delivery where the consumer
/// writes index.html themselves.
/// </summary>
/// <remarks>
/// Console assertions carry the weight here: a blocked font or language worker leaves a page that
/// still looks right, and only says so in the console.
/// </remarks>
[TestFixture]
[Category("Browser")]
public class CspTests :
    BrowserFixture
{
    protected override string? ContentSecurityPolicy =>
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "worker-src 'self' blob:";

    [Test]
    public async Task TheIdeBootsUnderTheDocumentedPolicy()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        Assert.That(languages, Does.Contain("graphql"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }

    /// <summary>
    /// The sidecar adds its stylesheet at runtime, so it is the one part of the library that could
    /// need a directive of its own. It does not: the injection is a link to a same-origin file, and
    /// this asserts the rules actually parsed rather than that the element exists.
    /// </summary>
    [Test]
    public async Task TheSidecarStylesheetLoadsUnderTheDocumentedPolicy()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        await page.ClickAsync("[data-testid='blazorql-sidecar-toggle']");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        var rules = await page.EvaluateAsync<int>(
            """
            () => {
                const sheet = Array.from(document.styleSheets)
                    .find(_ => (_.href || '').includes('blazorql-sidecar.css'));
                if (!sheet) {
                    return -1;
                }

                try {
                    return sheet.cssRules.length;
                } catch {
                    return 0;
                }
            }
            """);

        Assert.That(rules, Is.GreaterThan(0));
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
