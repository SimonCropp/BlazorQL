/// <summary>
/// The foundation smoke: the published sample boots with the BlazorMonaco editor stack — Monaco
/// mounted, graphql/json languages present, and, decisively, a console free of errors, which is
/// where asset and MIME failures land while the page otherwise looks fine.
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
/// The same smoke with the app mounted under a sub-path — how GitHub Pages hosts it. Every asset
/// reference resolves through the base href, and this is the fixture that keeps it that way.
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
