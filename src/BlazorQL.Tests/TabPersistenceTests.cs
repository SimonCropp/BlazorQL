
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
    public async Task SerializeNeverIncludesTheResponse()
    {
        var store = BuildStore();
        var json = store.Serialize(includeHeaders: true);

        await Assert.That(json).DoesNotContain("response");
        await Assert.That(json).DoesNotContain("""{"data": {}}""");
    }

    [Test]
    public async Task HeadersAreGatedOnThePersistFlag()
    {
        var store = BuildStore();

        await Assert.That(store.Serialize(includeHeaders: true)).Contains("secret");
        await Assert.That(store.Serialize(includeHeaders: false)).DoesNotContain("secret");
    }

    [Test]
    public async Task RoundTripsTabsAndActiveIndex()
    {
        var store = BuildStore();
        store.Activate(1);

        var restored = new TabStore();
        await Assert.That(restored.TryRestore(store.Serialize(includeHeaders: true))).IsTrue();

        await Assert.That(restored.Tabs).Count().IsEqualTo(2);
        await Assert.That(restored.ActiveIndex).IsEqualTo(1);
        await Assert.That(restored.Tabs[0].Query).IsEqualTo("query One { id }");
        await Assert.That(restored.Tabs[0].Variables).IsEqualTo("""{"a": 1}""");
        await Assert.That(restored.Tabs[0].Headers).IsEqualTo("""{"authorization": "secret"}""");
        await Assert.That(restored.Tabs[0].Response).IsEmpty();
        await Assert.That(restored.Tabs[1].OperationName).IsEqualTo("Two");
        await Assert.That(restored.Tabs[1].RenameOverride).IsEqualTo("Renamed");
    }

    [Test]
    public async Task RestoreClampsAnOutOfRangeActiveIndex()
    {
        var json =
            """
            {"activeTabIndex": 9, "tabs": [{"id": "5a0c5f19-6a15-4b3c-9f36-51f2af6a8e64", "query": "{ id }", "variables": ""}]}
            """;

        var store = new TabStore();
        await Assert.That(store.TryRestore(json)).IsTrue();
        await Assert.That(store.ActiveIndex).IsZero();
    }

    [Test]
    public async Task InvalidJsonLeavesTheStoreUntouched()
    {
        var store = new TabStore();
        await Assert.That(store.TryRestore("{oops")).IsFalse();
        await Assert.That(store.TryRestore(null)).IsFalse();
        await Assert.That(store.TryRestore("")).IsFalse();
        await Assert.That(store.TryRestore("""{"activeTabIndex": 0, "tabs": []}""")).IsFalse();
        await Assert.That(store.Tabs).IsEmpty();
    }
}