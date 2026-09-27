/// <summary>
/// The trailing-edge debounce behind every editor-change handler. Nothing awaits the task it
/// starts, so what it does with a failure is the whole of what anyone will ever learn about one.
/// </summary>
public class DebouncerTests
{
    [Test]
    public async Task OnlyTheLastActionInTheWindowRuns()
    {
        List<int> ran = [];
        using var debouncer = new Debouncer(20);

        debouncer.Run(() =>
        {
            ran.Add(1);
            return Task.CompletedTask;
        });
        debouncer.Run(() =>
        {
            ran.Add(2);
            return Task.CompletedTask;
        });

        await WaitFor(() => ran.Count > 0);

        await Assert.That(ran).IsEquivalentTo(lastOnly, CollectionOrdering.Matching);
    }

    static readonly int[] lastOnly = [2];

    [Test]
    public async Task DisposeCancelsAPendingAction()
    {
        var ran = false;
        var debouncer = new Debouncer(20);
        debouncer.Run(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        debouncer.Dispose();

        await Task.Delay(200);

        await Assert.That(ran).IsFalse();
    }

    [Test]
    public async Task CancelDropsAPendingAction()
    {
        var ran = false;
        using var debouncer = new Debouncer(20);

        debouncer.Run(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        debouncer.Cancel();
        await Task.Delay(200);

        await Assert.That(ran).IsFalse();
    }

    /// <summary>Cancel closes a window rather than the debouncer, unlike Dispose.</summary>
    [Test]
    public async Task ARunAfterCancelStillHappens()
    {
        var ran = false;
        using var debouncer = new Debouncer(20);

        debouncer.Run(() => Task.CompletedTask);
        debouncer.Cancel();
        debouncer.Run(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await WaitFor(() => ran);

        await Assert.That(ran).IsTrue();
    }

    // Console.Error is process-wide, so nothing else may run while it is swapped out.
    [Test]
    [NotInParallel]
    public async Task AFailingActionIsReportedRatherThanLost()
    {
        var written = new StringWriter();
        var original = Console.Error;
#pragma warning disable TUnit0055
        Console.SetError(written);
#pragma warning restore TUnit0055
        try
        {
            using var debouncer = new Debouncer(20);
            debouncer.Run(() => throw new InvalidOperationException("the editor is gone"));

            await WaitFor(() => written.ToString().Length > 0);
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetError(original);
#pragma warning restore TUnit0055
        }

        await Assert.That(written.ToString()).Contains("the editor is gone");
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