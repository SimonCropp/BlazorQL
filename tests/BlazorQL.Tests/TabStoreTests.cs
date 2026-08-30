[TestFixture]
public class TabStoreTests
{
    [Test]
    public void TitlePrefersRenameOverOperationNameOverQuery()
    {
        var tab = new TabState
        {
            Query = "query FromQuery { id }",
            OperationName = "FromRun",
            RenameOverride = "Renamed"
        };
        Assert.That(TabStore.Title(tab), Is.EqualTo("Renamed"));

        tab.RenameOverride = null;
        Assert.That(TabStore.Title(tab), Is.EqualTo("FromRun"));

        tab.OperationName = null;
        Assert.That(TabStore.Title(tab), Is.EqualTo("FromQuery"));
    }

    [Test]
    public void TitleSkipsCommentLines()
    {
        var tab = new TabState
        {
            Query =
                """
                # query Commented
                mutation DoThing { setString(value: "x") }
                """
        };
        Assert.That(TabStore.Title(tab), Is.EqualTo("DoThing"));
    }

    [Test]
    public void TitleFallsBackToUntitled()
    {
        var tab = new TabState
        {
            Query = "{ id }"
        };
        Assert.That(TabStore.Title(tab), Is.EqualTo("<untitled>"));
    }

    [Test]
    public void CloseKeepsTheActiveTabSensible()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");
        store.Add("three");
        Assert.That(store.ActiveIndex, Is.EqualTo(2));

        // Closing an earlier tab shifts the active index with the list.
        store.Close(0);
        Assert.That(store.ActiveIndex, Is.EqualTo(1));
        Assert.That(store.Active.Query, Is.EqualTo("three"));

        // Closing the active last tab activates the neighbour.
        store.Close(1);
        Assert.That(store.ActiveIndex, Is.Zero);
        Assert.That(store.Active.Query, Is.EqualTo("two"));
    }

    [Test]
    public void ActivateSwitchesTheActiveTab()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");
        store.Activate(0);
        Assert.That(store.Active.Query, Is.EqualTo("one"));
    }
}
