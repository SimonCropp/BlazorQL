[TestFixture]
public class TabPersistenceTests
{
    static TabStore BuildStore()
    {
        var store = new TabStore();
        var first = store.Add("query One { id }");
        first.Variables = """{"a": 1}""";
        first.Headers = """{"authorization": "secret"}""";
        first.Response = """{"data": {}}""";
        var second = store.Add("query Two { test }");
        second.OperationName = "Two";
        second.RenameOverride = "Renamed";
        return store;
    }

    [Test]
    public void SerializeNeverIncludesTheResponse()
    {
        var store = BuildStore();
        var json = store.Serialize(includeHeaders: true);

        Assert.That(json, Does.Not.Contain("response"));
        Assert.That(json, Does.Not.Contain("""{"data": {}}"""));
    }

    [Test]
    public void HeadersAreGatedOnThePersistFlag()
    {
        var store = BuildStore();

        Assert.That(store.Serialize(includeHeaders: true), Does.Contain("secret"));
        Assert.That(store.Serialize(includeHeaders: false), Does.Not.Contain("secret"));
    }

    [Test]
    public void RoundTripsTabsAndActiveIndex()
    {
        var store = BuildStore();
        store.Activate(1);

        var restored = new TabStore();
        Assert.That(restored.TryRestore(store.Serialize(includeHeaders: true)), Is.True);

        Assert.That(restored.Tabs, Has.Count.EqualTo(2));
        Assert.That(restored.ActiveIndex, Is.EqualTo(1));
        Assert.That(restored.Tabs[0].Query, Is.EqualTo("query One { id }"));
        Assert.That(restored.Tabs[0].Variables, Is.EqualTo("""{"a": 1}"""));
        Assert.That(restored.Tabs[0].Headers, Is.EqualTo("""{"authorization": "secret"}"""));
        Assert.That(restored.Tabs[0].Response, Is.Empty);
        Assert.That(restored.Tabs[1].OperationName, Is.EqualTo("Two"));
        Assert.That(restored.Tabs[1].RenameOverride, Is.EqualTo("Renamed"));
    }

    [Test]
    public void RestoreClampsAnOutOfRangeActiveIndex()
    {
        var json =
            """
            {"activeTabIndex": 9, "tabs": [{"id": "5a0c5f19-6a15-4b3c-9f36-51f2af6a8e64", "query": "{ id }", "variables": ""}]}
            """;

        var store = new TabStore();
        Assert.That(store.TryRestore(json), Is.True);
        Assert.That(store.ActiveIndex, Is.Zero);
    }

    [Test]
    public void InvalidJsonLeavesTheStoreUntouched()
    {
        var store = new TabStore();
        Assert.That(store.TryRestore("{oops"), Is.False);
        Assert.That(store.TryRestore(null), Is.False);
        Assert.That(store.TryRestore(""), Is.False);
        Assert.That(store.TryRestore("""{"activeTabIndex": 0, "tabs": []}"""), Is.False);
        Assert.That(store.Tabs, Is.Empty);
    }
}
