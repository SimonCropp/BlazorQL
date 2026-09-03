/// <summary>
/// The trailing-edge debounce behind every editor-change handler. Nothing awaits the task it
/// starts, so what it does with a failure is the whole of what anyone will ever learn about one.
/// </summary>
[TestFixture]
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

        Assert.That(ran, Is.EqualTo(lastOnly));
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

        Assert.That(ran, Is.False);
    }

    [Test]
    public async Task AFailingActionIsReportedRatherThanLost()
    {
        var written = new StringWriter();
        var original = Console.Error;
        Console.SetError(written);
        try
        {
            using var debouncer = new Debouncer(20);
            debouncer.Run(() => throw new InvalidOperationException("the editor is gone"));

            await WaitFor(() => written.ToString().Length > 0);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.That(written.ToString(), Does.Contain("the editor is gone"));
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
