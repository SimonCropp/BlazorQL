
public class TabStoreTests
{
    [Test]
    public async Task TitlePrefersRenameOverOperationNameOverQuery()
    {
        var tab = new TabState
        {
            Query = "query FromQuery { id }",
            OperationName = "FromRun",
            RenameOverride = "Renamed"
        };
        await Assert.That(TabStore.Title(tab)).IsEqualTo("Renamed");

        tab.RenameOverride = null;
        await Assert.That(TabStore.Title(tab)).IsEqualTo("FromRun");

        tab.OperationName = null;
        await Assert.That(TabStore.Title(tab)).IsEqualTo("FromQuery");
    }

    [Test]
    public async Task TitleSkipsCommentLines()
    {
        var tab = new TabState
        {
            Query =
                """
                # query Commented
                mutation DoThing { setString(value: "x") }
                """
        };
        await Assert.That(TabStore.Title(tab)).IsEqualTo("DoThing");
    }

    [Test]
    public async Task TitleFallsBackToUntitled()
    {
        var tab = new TabState
        {
            Query = "{ id }"
        };
        await Assert.That(TabStore.Title(tab)).IsEqualTo("<untitled>");
    }

    /// <summary>
    /// A comment that says "query" is still a comment. This is the shape that costs the most to
    /// answer — every line tried and rejected — so it is the one most likely to tempt a shortcut.
    /// </summary>
    [Test]
    public async Task TitleIgnoresAKeywordThatOnlyAppearsInAComment()
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
        await Assert.That(TabStore.Title(tab)).IsEqualTo("<untitled>");
    }

    /// <summary>The keyword is not word-bounded, and the last match on the line is the one taken.</summary>
    [Test]
    public async Task TitleTakesTheLastDeclarationOnALine()
    {
        var tab = new TabState
        {
            Query = "query First { id } query Second { id }"
        };
        await Assert.That(TabStore.Title(tab)).IsEqualTo("Second");
    }

    [Test]
    public async Task CloseKeepsTheActiveTabSensible()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");
        store.Add("three");
        await Assert.That(store.ActiveIndex).IsEqualTo(2);

        // Closing an earlier tab shifts the active index with the list.
        store.Close(0);
        await Assert.That(store.ActiveIndex).IsEqualTo(1);
        await Assert.That(store.Active.Query).IsEqualTo("three");

        // Closing the active last tab activates the neighbour.
        store.Close(1);
        await Assert.That(store.ActiveIndex).IsZero();
        await Assert.That(store.Active.Query).IsEqualTo("two");
    }

    [Test]
    public async Task ActivateSwitchesTheActiveTab()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");
        store.Activate(0);
        await Assert.That(store.Active.Query).IsEqualTo("one");
    }

    /// <summary>
    /// The tab bar hides the close button for a lone tab, so this is unreachable through the UI --
    /// but the store is what has to hold the invariant. Without one, ActiveIndex went to -1 and
    /// Active threw for everything that read it afterwards.
    /// </summary>
    [Test]
    public async Task ClosingTheLastTabIsRefused()
    {
        var store = new TabStore();
        store.Add("only");

        await Assert.That(store.Close(0)).IsFalse();
        await Assert.That(store.Tabs).Count().IsEqualTo(1);
        await Assert.That(store.ActiveIndex).IsZero();
        await Assert.That(store.Active.Query).IsEqualTo("only");
    }

    [Test]
    public async Task ClosingDownToOneTabStops()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");

        await Assert.That(store.Close(1)).IsTrue();
        await Assert.That(store.Close(0)).IsFalse();
        await Assert.That(store.Active.Query).IsEqualTo("one");
    }

    [Test]
    public async Task DuplicateInsertsACopyBesideTheSourceAndActivatesIt()
    {
        var store = new TabStore();
        var source = store.Add("query One($x: Int) { id }", """{"Authorization": "token"}""");
        source.Variables = """{"x": 1}""";
        source.OperationName = "One";
        source.Response = """{"data": {"id": "abc123"}}""";
        source.RenameOverride = "Renamed";
        store.Add("two");

        var copy = store.Duplicate(0);

        await Assert.That(store.Tabs).Count().IsEqualTo(3);
        await Assert.That(store.ActiveIndex).IsEqualTo(1);
        await Assert.That(store.Tabs[1]).IsSameReferenceAs(copy);
        // Beside its source rather than at the end, pushing the tab that followed it along.
        await Assert.That(store.Tabs[2].Query).IsEqualTo("two");
        await Assert.That(copy.Id).IsNotEqualTo(source.Id);
        // Everything but the id. Compared as records, so a member TabState gains later is covered too.
        await Assert.That(copy with {Id = source.Id}).IsEqualTo(source);
    }

    [Test]
    public async Task EditingADuplicateLeavesTheSourceAlone()
    {
        var store = new TabStore();
        var source = store.Add("query One { id }");

        var copy = store.Duplicate(0);
        copy.Query = "query Changed { id }";
        copy.Variables = """{"x": 2}""";

        await Assert.That(source.Query).IsEqualTo("query One { id }");
        await Assert.That(source.Variables).IsEmpty();
    }

    [Test]
    public async Task DuplicatingTheLastTabAppendsIt()
    {
        var store = new TabStore();
        store.Add("one");
        store.Add("two");

        store.Duplicate(1);

        await Assert.That(store.Tabs).Count().IsEqualTo(3);
        await Assert.That(store.ActiveIndex).IsEqualTo(2);
        await Assert.That(store.Active.Query).IsEqualTo("two");
    }

    /// <summary>
    /// What closing a tab leans on while a host's confirmation is outstanding: the index the host was
    /// asked about can have moved by the time the answer arrives.
    /// </summary>
    [Test]
    public async Task IndexOfFollowsATabThatMoved()
    {
        var store = new TabStore();
        store.Add("one");
        var two = store.Add("two");

        store.Duplicate(0);
        await Assert.That(store.IndexOf(two.Id)).IsEqualTo(2);

        store.Close(2);
        await Assert.That(store.IndexOf(two.Id)).IsEqualTo(-1);
    }

    /// <summary>
    /// A share link arrives on boot, after storage has restored the user's tabs, so the active tab
    /// holds their work. The link used to be written into it, and the next persist made that
    /// permanent.
    /// </summary>
    [Test]
    public async Task OpenNeverWritesOverTheActiveTab()
    {
        var store = new TabStore();
        var mine = store.Add("query Mine { id }");
        mine.Variables = """{"mine": 1}""";

        var opened = store.Open("query Shared { id }", """{"shared": 1}""", "x-default: 1", "welcome");

        using (Assert.Multiple())
        {
            await Assert.That(store.Tabs).Count().IsEqualTo(2);
            await Assert.That(mine.Query).IsEqualTo("query Mine { id }");
            await Assert.That(mine.Variables).IsEqualTo("""{"mine": 1}""");
            await Assert.That(store.ActiveIndex).IsEqualTo(1);
            await Assert.That(store.Active).IsSameReferenceAs(opened);
            await Assert.That(opened.Query).IsEqualTo("query Shared { id }");
            await Assert.That(opened.Variables).IsEqualTo("""{"shared": 1}""");
            // What any new tab starts with.
            await Assert.That(opened.Headers).IsEqualTo("x-default: 1");
        }
    }

    /// <summary>Opening the same link twice, or one for a tab already open, does not pile up copies.</summary>
    [Test]
    public async Task OpenSwitchesToATabAlreadyHoldingTheLink()
    {
        var store = new TabStore();
        store.Add("one");
        var shared = store.Add("query Shared { id }");
        shared.Variables = "{}";
        store.Add("three");

        var opened = store.Open("query Shared { id }", "{}", "", "welcome");

        using (Assert.Multiple())
        {
            await Assert.That(store.Tabs).Count().IsEqualTo(3);
            await Assert.That(store.ActiveIndex).IsEqualTo(1);
            await Assert.That(opened).IsSameReferenceAs(shared);
        }
    }

    [Test]
    public async Task OpenTellsTabsApartByTheirVariables()
    {
        var store = new TabStore();
        var first = store.Add("query Shared($id: ID) { node(id: $id) { id } }");
        first.Variables = """{"id": "1"}""";

        store.Open("query Shared($id: ID) { node(id: $id) { id } }", """{"id": "2"}""", "", "welcome");

        using (Assert.Multiple())
        {
            await Assert.That(store.Tabs).Count().IsEqualTo(2);
            await Assert.That(first.Variables).IsEqualTo("""{"id": "1"}""");
            await Assert.That(store.ActiveIndex).IsEqualTo(1);
        }
    }

    /// <summary>
    /// A first visit through a link: storage was empty, so the only tab is the seeded welcome text,
    /// and the link takes it over rather than opening beside it. Its headers are kept: the link
    /// carries none.
    /// </summary>
    [Test]
    public async Task OpenTakesOverAnUntouchedWelcomeTab()
    {
        var store = new TabStore();
        var welcome = store.Add("welcome", "x-default: 1");

        var opened = store.Open("query Shared { id }", "{}", "x-default: 1", "welcome");

        using (Assert.Multiple())
        {
            await Assert.That(store.Tabs).Count().IsEqualTo(1);
            await Assert.That(opened).IsSameReferenceAs(welcome);
            await Assert.That(welcome.Query).IsEqualTo("query Shared { id }");
            await Assert.That(welcome.Variables).IsEqualTo("{}");
            await Assert.That(welcome.Headers).IsEqualTo("x-default: 1");
        }
    }

    /// <summary>The welcome tab is only free to take while the user has done nothing with it.</summary>
    [Test]
    public async Task OpenLeavesAWelcomeTabTheUserHasTouched()
    {
        var withVariables = new TabStore();
        withVariables.Add("welcome").Variables = """{"a": 1}""";
        withVariables.Open("query Shared { id }", "", "", "welcome");

        var renamed = new TabStore();
        renamed.Add("welcome").RenameOverride = "Keep";
        renamed.Open("query Shared { id }", "", "", "welcome");

        using (Assert.Multiple())
        {
            await Assert.That(withVariables.Tabs).Count().IsEqualTo(2);
            await Assert.That(withVariables.Tabs[0].Query).IsEqualTo("welcome");
            await Assert.That(renamed.Tabs).Count().IsEqualTo(2);
            await Assert.That(renamed.Tabs[0].Query).IsEqualTo("welcome");
        }
    }
}