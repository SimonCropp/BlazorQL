[TestFixture]
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
    public void RecordsAnExecution()
    {
        var store = BuildStore();
        store.Record("{ id }", """{"a": 1}""", null, "Op");

        Assert.That(store.Items, Has.Count.EqualTo(1));
        Assert.That(store.Items[0].Query, Is.EqualTo("{ id }"));
        Assert.That(store.Items[0].Variables, Is.EqualTo("""{"a": 1}"""));
        Assert.That(store.Items[0].OperationName, Is.EqualTo("Op"));
    }

    [Test]
    public void SkipsEmptyAndWhitespaceQueries()
    {
        var store = BuildStore();
        store.Record("", null, null, null);
        store.Record("   \n\t", null, null, null);

        Assert.That(store.Items, Is.Empty);
    }

    [Test]
    public void SkipsQueriesThatDoNotParse()
    {
        var store = BuildStore(parses: _ => _ != "{ broken");
        store.Record("{ broken", null, null, null);
        store.Record("{ id }", null, null, null);

        Assert.That(store.Items, Has.Count.EqualTo(1));
        Assert.That(store.Items[0].Query, Is.EqualTo("{ id }"));
    }

    [Test]
    public void SkipsOversizedQueries()
    {
        var store = BuildStore();
        store.Record($"{{ {new string('a', 100_001)} }}", null, null, null);

        Assert.That(store.Items, Is.Empty);
    }

    [Test]
    public void SkipsAnExactRepeatOfTheHead()
    {
        var store = BuildStore();
        store.Record("{ id }", """{"a": 1}""", "{}", null);
        store.Record("{ id }", """{"a": 1}""", "{}", null);

        Assert.That(store.Items, Has.Count.EqualTo(1));

        // Changing any of query/variables/headers records again.
        store.Record("{ id }", """{"a": 2}""", "{}", null);
        Assert.That(store.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public void EvictsTheOldestBeyondMaxLength()
    {
        var store = BuildStore(maxLength: 3);
        for (var i = 0; i < 5; i++)
        {
            store.Record($"{{ q{i} }}", null, null, null);
        }

        Assert.That(store.Items, Has.Count.EqualTo(3));
        // Newest first, the two oldest evicted.
        string[] expected = ["{ q4 }", "{ q3 }", "{ q2 }"];
        Assert.That(store.Items.Select(_ => _.Query), Is.EqualTo(expected));
    }

    [Test]
    public void FavoritesAreUnlimitedAndUncapped()
    {
        var store = BuildStore(maxLength: 2);
        for (var i = 0; i < 4; i++)
        {
            store.Record($"{{ q{i} }}", null, null, null);
            store.ToggleFavorite(store.Items[0]);
        }

        Assert.That(store.Favorites, Has.Count.EqualTo(4));
        Assert.That(store.Items, Is.Empty);
    }

    [Test]
    public void ToggleFavoriteMovesBetweenLists()
    {
        var store = BuildStore();
        store.Record("{ id }", null, null, null);
        var item = store.Items[0];

        store.ToggleFavorite(item);
        Assert.That(item.Favorite, Is.True);
        Assert.That(store.Items, Is.Empty);
        Assert.That(store.Favorites, Is.EqualTo([item]));

        store.ToggleFavorite(item);
        Assert.That(item.Favorite, Is.False);
        Assert.That(store.Favorites, Is.Empty);
        Assert.That(store.Items, Is.EqualTo([item]));
    }

    [Test]
    public void ClearOnlyRemovesNonFavorites()
    {
        var store = BuildStore();
        store.Record("{ keep }", null, null, null);
        store.ToggleFavorite(store.Items[0]);
        store.Record("{ drop }", null, null, null);

        store.ClearNonFavorites();

        Assert.That(store.Items, Is.Empty);
        Assert.That(store.Favorites, Has.Count.EqualTo(1));
    }

    [Test]
    public void PersistsAndReloads()
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
        Assert.That(reloaded.Items, Has.Count.EqualTo(1));
        Assert.That(reloaded.Items[0].Label, Is.EqualTo("My label"));
        Assert.That(reloaded.Items[0].Variables, Is.EqualTo("""{"x": 1}"""));
        Assert.That(reloaded.Favorites, Has.Count.EqualTo(1));
        Assert.That(reloaded.Favorites[0].Favorite, Is.True);
    }

    [Test]
    public void MatchesSearchesQueryLabelAndOperationName()
    {
        var item = new HistoryItem
        {
            Query = "query FindThings { id }",
            Label = "My Label",
            OperationName = "FindThings"
        };

        Assert.That(HistoryStore.Matches(item, ""), Is.True);
        Assert.That(HistoryStore.Matches(item, "findthings"), Is.True);
        Assert.That(HistoryStore.Matches(item, "my label"), Is.True);
        Assert.That(HistoryStore.Matches(item, "{ id }"), Is.True);
        Assert.That(HistoryStore.Matches(item, "nowhere"), Is.False);
    }

    [Test]
    public void DisplayTextPrefersLabelThenOperationNameThenCondensedQuery()
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
        Assert.That(HistoryStore.DisplayText(item), Is.EqualTo("Labelled"));

        item.Label = null;
        Assert.That(HistoryStore.DisplayText(item), Is.EqualTo("FromRun"));

        var unnamed = item with
        {
            OperationName = null
        };
        Assert.That(HistoryStore.DisplayText(unnamed), Is.EqualTo("query Long { id }"));
    }
}
