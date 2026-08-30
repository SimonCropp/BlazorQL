/// <summary>bUnit coverage for the history pane over an in-memory-backed store.</summary>
[TestFixture]
public class HistoryPaneTests
{
    static HistoryStore BuildStore()
    {
        var store = new HistoryStore(new(new InMemoryStorageBackend()), _ => true);
        store.Record("{ first }", null, null, null);
        store.Record("{ second }", null, null, null);
        store.Record("{ third }", null, null, null);
        return store;
    }

    static IRenderedComponent<HistoryPane> Render(BunitContext context, HistoryStore store) =>
        context.Render<HistoryPane>(_ => _.Add(component => component.Store, store));

    static IReadOnlyList<string> ItemTexts(IRenderedComponent<HistoryPane> cut) =>
        [.. cut.FindAll("[data-testid='history-item']").Select(_ => _.TextContent)];

    [Test]
    public void ItemsRenderNewestFirst()
    {
        using var context = new BunitContext();
        var cut = Render(context, BuildStore());

        string[] expected = ["{ third }", "{ second }", "{ first }"];
        Assert.That(ItemTexts(cut), Is.EqualTo(expected));
    }

    [Test]
    public void FavoritesRenderFirst()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        // Favorite the oldest item; it moves to the top block.
        store.ToggleFavorite(store.Items[2]);
        var cut = Render(context, store);

        string[] expected = ["{ first }", "{ third }", "{ second }"];
        Assert.That(ItemTexts(cut), Is.EqualTo(expected));
        Assert.That(cut.FindAll(".blazorql-history-spacer"), Has.Count.EqualTo(1));
    }

    [Test]
    public void SelectRaisesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        HistoryItem? selected = null;
        var cut = context.Render<HistoryPane>(_ => _
            .Add(component => component.Store, store)
            .Add(component => component.OnSelect, item => selected = item));

        cut.FindAll("[data-testid='history-item']")[0].Click();
        Assert.That(selected, Is.SameAs(store.Items[0]));
    }

    [Test]
    public void LabelEditCommitsOnEnter()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Edit label']")[0].Click();
        var input = cut.Find("[data-testid='history-label-input']");
        input.Input("Renamed");
        input.KeyDown("Enter");

        Assert.That(store.Items[0].Label, Is.EqualTo("Renamed"));
        Assert.That(ItemTexts(cut)[0], Is.EqualTo("Renamed"));
    }

    [Test]
    public void LabelEditCancelsOnEscape()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Edit label']")[0].Click();
        var input = cut.Find("[data-testid='history-label-input']");
        input.Input("Abandoned");
        input.KeyDown("Escape");

        Assert.That(store.Items[0].Label, Is.Null);
        Assert.That(ItemTexts(cut)[0], Is.EqualTo("{ third }"));
    }

    [Test]
    public void SearchFiltersCaseInsensitively()
    {
        using var context = new BunitContext();
        var cut = Render(context, BuildStore());

        cut.Find("[data-testid='history-search']").Input("SECOND");
        string[] expected = ["{ second }"];
        Assert.That(ItemTexts(cut), Is.EqualTo(expected));
    }

    [Test]
    public void FavoriteToggleMovesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Add favorite']")[1].Click();
        Assert.That(store.Favorites[0].Query, Is.EqualTo("{ second }"));
        Assert.That(ItemTexts(cut)[0], Is.EqualTo("{ second }"));
    }

    [Test]
    public void DeleteRemovesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Delete from history']")[0].Click();
        string[] expected = ["{ second }", "{ first }"];
        Assert.That(ItemTexts(cut), Is.EqualTo(expected));
    }

    [Test]
    public void ClearDisablesWhenEmpty()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        var clear = cut.Find("[data-testid='history-clear']");
        Assert.That(clear.HasAttribute("disabled"), Is.False);

        clear.Click();
        Assert.That(store.Items, Is.Empty);
        Assert.That(cut.Find("[data-testid='history-clear']").HasAttribute("disabled"), Is.True);
    }
}
