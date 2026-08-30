/// <summary>
/// The foundation smoke: the published sample boots with the vendored editor stack — Monaco
/// mounted, graphql/json languages registered, and, decisively, a console free of errors, which is
/// where asset, MIME, and worker failures land while the page otherwise looks fine.
/// </summary>
[TestFixture]
[Category("Browser")]
public class BootTests :
    BrowserFixture
{
    [Test]
    public async Task BootsCleanly()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(l => l.id).filter(l => ['graphql', 'json'].includes(l))");
        Assert.That(languages, Is.EquivalentTo(["graphql", "json"]));

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}

/// <summary>
/// The same smoke with the app mounted under a sub-path — how GitHub Pages hosts it. Worker urls
/// and vendored imports are all relative, and this is the fixture that keeps them that way.
/// </summary>
[TestFixture]
[Category("Browser")]
public class SubpathBootTests :
    BrowserFixture
{
    protected override string PathBase => "/BlazorQL";

    [Test]
    public async Task BootsCleanlyUnderTheSubpath()
    {
        var page = await NewPageAsync();
        await page.GoToAppAsync(BaseUrl);

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(l => l.id).filter(l => ['graphql', 'json'].includes(l))");
        Assert.That(languages, Is.EquivalentTo(["graphql", "json"]));

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
