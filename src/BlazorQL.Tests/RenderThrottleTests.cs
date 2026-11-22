/// <summary>
/// The throttle behind a streamed response: a burst renders once per interval, and whatever the
/// burst ends on is always rendered, because that is the response left on screen.
/// </summary>
public class RenderThrottleTests
{
    sealed class Recorder
    {
        public int State { get; set; }

        public List<int> Rendered { get; } = [];

        public Task Render()
        {
            Rendered.Add(State);
            return Task.CompletedTask;
        }
    }

    static async Task WaitFor(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            await Task.Delay(10);
        }
    }

    [Test]
    public async Task TheFirstChangeRendersAtOnce()
    {
        var recorder = new Recorder {State = 1};
        var throttle = new RenderThrottle(TimeSpan.FromSeconds(10), recorder.Render);

        await throttle.Changed();

        await Assert.That(recorder.Rendered).IsEquivalentTo([1]);
    }

    /// <summary>A thousand parts in a burst cost a handful of renders, not a thousand.</summary>
    [Test]
    public async Task ABurstRendersOncePerInterval()
    {
        var recorder = new Recorder();
        var throttle = new RenderThrottle(TimeSpan.FromMilliseconds(50), recorder.Render);

        for (var part = 1; part <= 1000; part++)
        {
            recorder.State = part;
            await throttle.Changed();
        }

        await throttle.Flush();

        await Assert.That(recorder.Rendered.Count).IsLessThan(10);
        await Assert.That(recorder.Rendered[^1]).IsEqualTo(1000);
    }

    /// <summary>
    /// A stream that goes quiet inside the interval still shows its last part, from the render
    /// scheduled for the interval's end, without anything further arriving.
    /// </summary>
    [Test]
    public async Task TheLastChangeOfABurstRendersWhenTheStreamGoesQuiet()
    {
        var recorder = new Recorder {State = 1};
        var throttle = new RenderThrottle(TimeSpan.FromMilliseconds(50), recorder.Render);
        await throttle.Changed();

        recorder.State = 2;
        await throttle.Changed();
        recorder.State = 3;
        await throttle.Changed();

        await WaitFor(() => recorder.Rendered.Count == 2);
        await Assert.That(recorder.Rendered).IsEquivalentTo([1, 3]);
    }

    /// <summary>Flushing with nothing new renders nothing, so the end of a stream costs no extra render.</summary>
    [Test]
    public async Task FlushWithNothingNewDoesNotRender()
    {
        var recorder = new Recorder {State = 1};
        var throttle = new RenderThrottle(TimeSpan.FromSeconds(10), recorder.Render);
        await throttle.Changed();

        await throttle.Flush();

        await Assert.That(recorder.Rendered).IsEquivalentTo([1]);
    }

    /// <summary>
    /// An error replaces the response, so a render still scheduled for the stream must not land
    /// after it and put the stream's state back.
    /// </summary>
    [Test]
    public async Task DiscardLeavesTheScheduledRenderNothingToDo()
    {
        var recorder = new Recorder {State = 1};
        var throttle = new RenderThrottle(TimeSpan.FromMilliseconds(50), recorder.Render);
        await throttle.Changed();
        recorder.State = 2;
        await throttle.Changed();

        throttle.Discard();
        await Task.Delay(200);

        await Assert.That(recorder.Rendered).IsEquivalentTo([1]);
    }

    /// <summary>A flush ahead of the scheduled render leaves that render nothing to do.</summary>
    [Test]
    public async Task AFlushBeforeTheScheduledRenderIsTheOnlyRender()
    {
        var recorder = new Recorder {State = 1};
        var throttle = new RenderThrottle(TimeSpan.FromMilliseconds(50), recorder.Render);
        await throttle.Changed();
        recorder.State = 2;
        await throttle.Changed();

        await throttle.Flush();
        await Task.Delay(200);

        await Assert.That(recorder.Rendered).IsEquivalentTo([1, 2]);
    }
}
