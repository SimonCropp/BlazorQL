/// <summary>
/// Browser storage is about 5 MB per origin, and a write past it is refused rather than throwing.
/// History kept every item whatever its size, and every caller ignored a refused write, so large
/// variables could fill the quota and tab state then stopped saving without a word.
/// </summary>
public class StorageQuotaTests
{
    /// <summary>Refuses a write that would take the total past the quota, as localStorage does.</summary>
    sealed class QuotaBackend(int quota) :
        IStorageBackend
    {
        readonly Dictionary<string, string> values = [];

        public int Used => values.Sum(_ => _.Key.Length + _.Value.Length);

        public string? Get(string key) =>
            values.GetValueOrDefault(key);

        public bool Set(string key, string value)
        {
            var current = values.TryGetValue(key, out var existing) ? key.Length + existing.Length : 0;
            if (Used - current + key.Length + value.Length > quota)
            {
                return false;
            }

            values[key] = value;
            return true;
        }

        public void Remove(string key) =>
            values.Remove(key);

        public IReadOnlyList<string> Keys() =>
            [.. values.Keys];
    }

    static HistoryStore History(StorageService storage) =>
        new(storage, _ => true);

    /// <summary>The query has had this limit all along; variables and headers are no different.</summary>
    [Test]
    public async Task OversizedVariablesOrHeadersAreNotRecorded()
    {
        var history = History(new(new InMemoryStorageBackend()));
        var huge = new string('x', 100_001);

        history.Record("{ a }", huge, null, null);
        history.Record("{ b }", null, huge, null);
        history.Record("{ c }", "{}", null, null);

        await Assert.That(history.Items.Select(_ => _.Query)).IsEquivalentTo(["{ c }"]);
    }

    /// <summary>
    /// A write the quota refuses evicts the oldest ordinary items until the rest fits, rather than
    /// leaving the stored history at whatever it last managed to write.
    /// </summary>
    [Test]
    public async Task AFullQuotaEvictsTheOldestItemsUntilHistoryFits()
    {
        var backend = new QuotaBackend(20_000);
        var storage = new StorageService(backend);
        var history = History(storage);
        var variables = $$"""{"v":"{{new string('x', 4_000)}}"}""";

        for (var index = 0; index < 10; index++)
        {
            history.Record($"{{ item{index} }}", variables, null, null);
        }

        // What is stored is what a fresh load sees.
        var reloaded = History(storage);
        await Assert.That(reloaded.Items).IsNotEmpty();
        await Assert.That(reloaded.Items[0].Query).IsEqualTo("{ item9 }");
        await Assert.That(reloaded.Items.Select(_ => _.Query)).IsEquivalentTo(history.Items.Select(_ => _.Query));
        await Assert.That(backend.Used).IsLessThanOrEqualTo(20_000);
    }

    /// <summary>Favorites are the user's choice, so room is never made by evicting one.</summary>
    [Test]
    public async Task MakingRoomNeverEvictsAFavorite()
    {
        var history = History(new(new InMemoryStorageBackend()));
        history.Record("{ kept }", null, null, null);
        history.ToggleFavorite(history.Items[0]);

        await Assert.That(history.MakeRoom()).IsFalse();
        await Assert.That(history.Favorites).Count().IsEqualTo(1);
    }

    /// <summary>
    /// A refused write is retried after each bit of room made, until it fits or there is nothing
    /// left to make room with: history yields to tab state, which matters more.
    /// </summary>
    [Test]
    public async Task AWriteRefusedForSpaceIsRetriedAsRoomIsMade()
    {
        var backend = new QuotaBackend(10_000);
        var storage = new StorageService(backend);
        storage.Set("filler", new('f', 9_000));

        var attempts = 0;
        var stored = storage.SetMakingRoom(
            "tabState",
            new('t', 5_000),
            () =>
            {
                attempts++;
                // Each call frees some of the filler, the way evicting a history item would.
                var filler = storage.Get("filler")!;
                storage.Set("filler", filler[..Math.Max(0, filler.Length - 2_000)]);
                return filler.Length > 0;
            });

        await Assert.That(stored).IsTrue();
        await Assert.That(storage.Get("tabState")).IsNotNull();
        await Assert.That(attempts).IsGreaterThan(0);
    }

    [Test]
    public async Task AWriteThatCannotFitReportsFailure()
    {
        var storage = new StorageService(new QuotaBackend(100));

        await Assert.That(storage.SetMakingRoom("tabState", new('t', 500), () => false)).IsFalse();
    }
}
