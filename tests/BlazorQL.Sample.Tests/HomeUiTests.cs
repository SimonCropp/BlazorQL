/// <summary>
/// The sample's default page — an ordinary Blazor app consuming the GraphQL schema through the
/// shared fetcher: its load-time query, mutation, and subscription, the sidecar capturing them,
/// and the links into the query explorer.
/// </summary>
[TestFixture]
[Category("Browser")]
public class HomeUiTests :
    BrowserFixture
{
    [Test]
    public async Task LoadsProfileAndEchoesMutation()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        var name = await page.Locator("[data-testid='home-name']").InnerTextAsync();
        Assert.That(name, Does.Contain("Mark"));
        Assert.That(name, Does.Contain("age 21"));
        var friends = page.Locator("[data-testid='home-friends'] li");
        Assert.That(await friends.CountAsync(), Is.EqualTo(4));
        Assert.That(await friends.First.InnerTextAsync(), Is.EqualTo("James"));

        await page.FillAsync("[data-testid='home-echo-input']", "hi from the tests");
        await page.ClickAsync("[data-testid='home-echo-send']");
        await page.WaitForSelectorAsync("[data-testid='home-echo-result']", 10);
        var echoed = await page.Locator("[data-testid='home-echo-result']").InnerTextAsync();
        Assert.That(echoed, Does.Contain("hi from the tests"));

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task SubscriptionStreamsGreetings()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        await page.ClickAsync("[data-testid='home-feed-toggle']");
        await page.WaitForSelectorAsync("[data-testid='home-greetings'] li", 15);

        // Stop mid-stream; the button falls back to Start once the stream has wound down.
        await page.ClickAsync("[data-testid='home-feed-toggle']");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=home-feed-toggle]').textContent.trim() === 'Start'",
            null,
            new() {Timeout = 10_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task SidecarCapturesTheAppsRequests()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        await page.ClickAsync("[data-testid='blazorql-sidecar-toggle']");
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar']", 10);

        var row = page.Locator("[data-testid='blazorql-sidecar-entries'] li", new() {HasTextString = "Profile"});
        await row.ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='blazorql-sidecar-detail']", 10);

        var query = await page.Locator("[data-testid='blazorql-sidecar-query']").InnerTextAsync();
        Assert.That(query, Does.Contain("query Profile"));
        var response = await page.Locator("[data-testid='blazorql-sidecar-response']").First.InnerTextAsync();
        Assert.That(response, Does.Contain("Mark"));

        // The captured request opens pre-populated in the explorer, in a new tab.
        var popup = await page.RunAndWaitForPopupAsync(() =>
            page.ClickAsync("[data-testid='blazorql-sidecar-ide-link']"));
        await popup.WaitForIdeReadyAsync();
        await popup.WaitForFunctionAsync(
            "() => monaco.editor.getModels().some(m => m.getValue().includes('query Profile'))",
            null,
            new() {Timeout = 30_000});

        Assert.That(ConsoleErrors(), Is.Empty);
    }

    [Test]
    public async Task ExplorerLinkOpensTheIde()
    {
        var page = await NewPageAsync();
        await page.GoToHomeAsync(BaseUrl);

        await page.ClickAsync("[data-testid='open-explorer']");
        await page.WaitForIdeReadyAsync();

        Assert.That(ConsoleErrors(), Is.Empty);
    }
}
