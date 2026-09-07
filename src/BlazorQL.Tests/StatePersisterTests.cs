/// <summary>
/// The two write paths behind tab state. An editor has already coalesced its own change burst by
/// the time it persists, so a second window in front of that write is latency and nothing else;
/// a click has not, so its window stays.
/// </summary>
[TestFixture]
public class StatePersisterTests
{
    [Test]
    public void NowWritesWithoutWaiting()
    {
        var writes = 0;
        using var persister = new StatePersister(() => writes++, 10_000);

        persister.Now();

        Assert.That(writes, Is.EqualTo(1));
    }

    /// <summary>
    /// The reason Now cancels rather than merely writing: a click's pending write, landing on
    /// state this one has already stored, would be a second write of the same thing.
    /// </summary>
    [Test]
    public async Task NowDropsAWriteAlreadyWaiting()
    {
        var writes = 0;
        using var persister = new StatePersister(() => writes++, 20);

        persister.Soon();
        persister.Now();
        await Task.Delay(200);

        Assert.That(writes, Is.EqualTo(1));
    }

    [Test]
    public async Task SoonWaitsForTheCallsToStop()
    {
        var writes = 0;
        using var persister = new StatePersister(() => writes++, 20);

        persister.Soon();
        persister.Soon();
        persister.Soon();

        Assert.That(writes, Is.Zero);

        await WaitFor(() => writes > 0);

        Assert.That(writes, Is.EqualTo(1));
    }

    /// <summary>A window that has closed is not a debouncer that has stopped.</summary>
    [Test]
    public async Task SoonStillWritesAfterANow()
    {
        var writes = 0;
        using var persister = new StatePersister(() => writes++, 20);

        persister.Now();
        persister.Soon();

        await WaitFor(() => writes > 1);

        Assert.That(writes, Is.EqualTo(2));
    }

    /// <summary>
    /// Disposal follows the IDE being torn down, and the state it would write belongs to editors
    /// that are already gone.
    /// </summary>
    [Test]
    public async Task DisposeDropsAWriteAlreadyWaiting()
    {
        var writes = 0;
        var persister = new StatePersister(() => writes++, 20);

        persister.Soon();
        persister.Dispose();
        await Task.Delay(200);

        Assert.That(writes, Is.Zero);
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
