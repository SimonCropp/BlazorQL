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

    /// <summary>
    /// A comment that says "query" is still a comment. This is the shape that costs the most to
    /// answer — every line tried and rejected — so it is the one most likely to tempt a shortcut.
    /// </summary>
    [Test]
    public void TitleIgnoresAKeywordThatOnlyAppearsInAComment()
    {
        var tab = new TabState
        {
            Query =
                """
                # the query below is anonymous
                {
                  id
                  isTest
                }
                """
        };
        Assert.That(TabStore.Title(tab), Is.EqualTo("<untitled>"));
    }

    /// <summary>The keyword is not word-bounded, and the last match on the line is the one taken.</summary>
    [Test]
    public void TitleTakesTheLastDeclarationOnALine()
    {
        var tab = new TabState
        {
            Query = "query First { id } query Second { id }"
        };
        Assert.That(TabStore.Title(tab), Is.EqualTo("Second"));
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

    /// <summary>
    /// The tab bar hides the close button for a lone tab, so this is unreachable through the UI --
    /// but the store is what has to hold the invariant. Without one, ActiveIndex went to -1 and
    /// Active threw for everything that read it afterwards.
    /// </summary>
    [Test]
    public void ClosingTheLastTabIsRefused()
    {
        var store = new TabStore();
        store.Add("only");

        Assert.That(store.Close(0), Is.False);
        Assert.That(store.Tabs, Has.Count.EqualTo(1));
        Assert.That(store.ActiveIndex, Is.Zero);
        Assert.That(store.Active.Query, Is.EqualTo("only"));
    }

    [Test]
    public void ClosingDownToOneTabStops()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");

        Assert.That(store.Close(1), Is.True);
        Assert.That(store.Close(0), Is.False);
        Assert.That(store.Active.Query, Is.EqualTo("one"));
    }
}
