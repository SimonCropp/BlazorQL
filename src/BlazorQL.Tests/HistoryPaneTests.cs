/// <summary>bUnit coverage for the history pane over an in-memory-backed store.</summary>
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
    public async Task ItemsRenderNewestFirst()
    {
        using var context = new BunitContext();
        var cut = Render(context, BuildStore());

        string[] expected = ["{ third }", "{ second }", "{ first }"];
        await Assert.That(ItemTexts(cut)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FavoritesRenderFirst()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        // Favorite the oldest item; it moves to the top block.
        store.ToggleFavorite(store.Items[2]);
        var cut = Render(context, store);

        string[] expected = ["{ first }", "{ third }", "{ second }"];
        await Assert.That(ItemTexts(cut)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(cut.FindAll(".blazorql-history-spacer")).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SelectRaisesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        HistoryItem? selected = null;
        var cut = context.Render<HistoryPane>(_ => _
            .Add(component => component.Store, store)
            .Add(component => component.OnSelect, item => selected = item));

        cut.FindAll("[data-testid='history-item']")[0].Click();
        await Assert.That(selected).IsSameReferenceAs(store.Items[0]);
    }

    [Test]
    public async Task LabelEditCommitsOnEnter()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Edit label']")[0].Click();
        var input = cut.Find("[data-testid='history-label-input']");
        input.Input("Renamed");
        input.KeyDown("Enter");

        await Assert.That(store.Items[0].Label).IsEqualTo("Renamed");
        await Assert.That(ItemTexts(cut)[0]).IsEqualTo("Renamed");
    }

    [Test]
    public async Task LabelEditCancelsOnEscape()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Edit label']")[0].Click();
        var input = cut.Find("[data-testid='history-label-input']");
        input.Input("Abandoned");
        input.KeyDown("Escape");

        await Assert.That(store.Items[0].Label).IsNull();
        await Assert.That(ItemTexts(cut)[0]).IsEqualTo("{ third }");
    }

    [Test]
    public async Task SearchFiltersCaseInsensitively()
    {
        using var context = new BunitContext();
        var cut = Render(context, BuildStore());

        cut.Find("[data-testid='history-search']").Input("SECOND");
        string[] expected = ["{ second }"];
        await Assert.That(ItemTexts(cut)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FavoriteToggleMovesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Add favorite']")[1].Click();
        await Assert.That(store.Favorites[0].Query).IsEqualTo("{ second }");
        await Assert.That(ItemTexts(cut)[0]).IsEqualTo("{ second }");
    }

    [Test]
    public async Task DeleteRemovesTheItem()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        cut.FindAll("[aria-label='Delete from history']")[0].Click();
        string[] expected = ["{ second }", "{ first }"];
        await Assert.That(ItemTexts(cut)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ClearDisablesWhenEmpty()
    {
        using var context = new BunitContext();
        var store = BuildStore();
        var cut = Render(context, store);

        var clear = cut.Find("[data-testid='history-clear']");
        await Assert.That(clear.HasAttribute("disabled")).IsFalse();

        clear.Click();
        await Assert.That(store.Items).IsEmpty();
        await Assert.That(cut.Find("[data-testid='history-clear']").HasAttribute("disabled")).IsTrue();
    }
}