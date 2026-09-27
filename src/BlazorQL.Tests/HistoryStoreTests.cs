
public class HistoryStoreTests
{
    static HistoryStore BuildStore(
        StorageService? storage = null,
        Func<string, bool>? parses = null,
        int maxLength = 20) =>
        new(
            storage ?? new(new InMemoryStorageBackend()),
            parses ?? (_ => true),
            maxLength);

    [Test]
    public async Task RecordsAnExecution()
    {
        var store = BuildStore();
        store.Record("{ id }", """{"a": 1}""", null, "Op");

        await Assert.That(store.Items).Count().IsEqualTo(1);
        await Assert.That(store.Items[0].Query).IsEqualTo("{ id }");
        await Assert.That(store.Items[0].Variables).IsEqualTo("""{"a": 1}""");
        await Assert.That(store.Items[0].OperationName).IsEqualTo("Op");
    }

    [Test]
    public async Task SkipsEmptyAndWhitespaceQueries()
    {
        var store = BuildStore();
        store.Record("", null, null, null);
        store.Record("   \n\t", null, null, null);

        await Assert.That(store.Items).IsEmpty();
    }

    [Test]
    public async Task SkipsQueriesThatDoNotParse()
    {
        var store = BuildStore(parses: _ => _ != "{ broken");
        store.Record("{ broken", null, null, null);
        store.Record("{ id }", null, null, null);

        await Assert.That(store.Items).Count().IsEqualTo(1);
        await Assert.That(store.Items[0].Query).IsEqualTo("{ id }");
    }

    [Test]
    public async Task SkipsOversizedQueries()
    {
        var store = BuildStore();
        store.Record($"{{ {new string('a', 100_001)} }}", null, null, null);

        await Assert.That(store.Items).IsEmpty();
    }

    [Test]
    public async Task SkipsAnExactRepeatOfTheHead()
    {
        var store = BuildStore();
        store.Record("{ id }", """{"a": 1}""", "{}", null);
        store.Record("{ id }", """{"a": 1}""", "{}", null);

        await Assert.That(store.Items).Count().IsEqualTo(1);

        // Changing any of query/variables/headers records again.
        store.Record("{ id }", """{"a": 2}""", "{}", null);
        await Assert.That(store.Items).Count().IsEqualTo(2);
    }

    [Test]
    public async Task EvictsTheOldestBeyondMaxLength()
    {
        var store = BuildStore(maxLength: 3);
        for (var i = 0; i < 5; i++)
        {
            store.Record($"{{ q{i} }}", null, null, null);
        }

        await Assert.That(store.Items).Count().IsEqualTo(3);
        // Newest first, the two oldest evicted.
        string[] expected = ["{ q4 }", "{ q3 }", "{ q2 }"];
        await Assert.That(store.Items.Select(_ => _.Query)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task FavoritesAreUnlimitedAndUncapped()
    {
        var store = BuildStore(maxLength: 2);
        for (var i = 0; i < 4; i++)
        {
            store.Record($"{{ q{i} }}", null, null, null);
            store.ToggleFavorite(store.Items[0]);
        }

        await Assert.That(store.Favorites).Count().IsEqualTo(4);
        await Assert.That(store.Items).IsEmpty();
    }

    [Test]
    public async Task ToggleFavoriteMovesBetweenLists()
    {
        var store = BuildStore();
        store.Record("{ id }", null, null, null);
        var item = store.Items[0];

        store.ToggleFavorite(item);
        await Assert.That(item.Favorite).IsTrue();
        await Assert.That(store.Items).IsEmpty();
        await Assert.That(store.Favorites).IsEquivalentTo([item], CollectionOrdering.Matching);

        store.ToggleFavorite(item);
        await Assert.That(item.Favorite).IsFalse();
        await Assert.That(store.Favorites).IsEmpty();
        await Assert.That(store.Items).IsEquivalentTo([item], CollectionOrdering.Matching);
    }

    [Test]
    public async Task ClearOnlyRemovesNonFavorites()
    {
        var store = BuildStore();
        store.Record("{ keep }", null, null, null);
        store.ToggleFavorite(store.Items[0]);
        store.Record("{ drop }", null, null, null);

        store.ClearNonFavorites();

        await Assert.That(store.Items).IsEmpty();
        await Assert.That(store.Favorites).Count().IsEqualTo(1);
    }

    [Test]
    public async Task PersistsAndReloads()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend);
        var store = BuildStore(storage);
        store.Record("{ fav }", null, null, null);
        store.ToggleFavorite(store.Items[0]);
        store.Record("{ plain }", """{"x": 1}""", null, "Plain");
        store.EditLabel(store.Items[0], "My label");

        // A fresh store over the same storage sees everything, favorite flags included.
        var reloaded = BuildStore(storage);
        await Assert.That(reloaded.Items).Count().IsEqualTo(1);
        await Assert.That(reloaded.Items[0].Label).IsEqualTo("My label");
        await Assert.That(reloaded.Items[0].Variables).IsEqualTo("""{"x": 1}""");
        await Assert.That(reloaded.Favorites).Count().IsEqualTo(1);
        await Assert.That(reloaded.Favorites[0].Favorite).IsTrue();
    }

    [Test]
    public async Task MatchesSearchesQueryLabelAndOperationName()
    {
        var item = new HistoryItem
        {
            Query = "query FindThings { id }",
            Label = "My Label",
            OperationName = "FindThings"
        };

        await Assert.That(HistoryStore.Matches(item, "")).IsTrue();
        await Assert.That(HistoryStore.Matches(item, "findthings")).IsTrue();
        await Assert.That(HistoryStore.Matches(item, "my label")).IsTrue();
        await Assert.That(HistoryStore.Matches(item, "{ id }")).IsTrue();
        await Assert.That(HistoryStore.Matches(item, "nowhere")).IsFalse();
    }

    [Test]
    public async Task DisplayTextPrefersLabelThenOperationNameThenCondensedQuery()
    {
        var item = new HistoryItem
        {
            Query =
                """
                # a comment line
                query  Long {
                  id
                }
                """,
            OperationName = "FromRun",
            Label = "Labelled"
        };
        await Assert.That(HistoryStore.DisplayText(item)).IsEqualTo("Labelled");

        item.Label = null;
        await Assert.That(HistoryStore.DisplayText(item)).IsEqualTo("FromRun");

        var unnamed = item with
        {
            OperationName = null
        };
        await Assert.That(HistoryStore.DisplayText(unnamed)).IsEqualTo("query Long { id }");
    }
}