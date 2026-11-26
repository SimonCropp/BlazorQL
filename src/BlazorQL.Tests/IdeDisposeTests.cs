/// <summary>
/// The IDE going away with introspection still in flight, as it does when the user navigates off
/// the page during a slow schema load. The load belonged to the component, so it has to stop, and
/// a result that lands anyway is no longer the component's to install.
/// </summary>
/// <remarks>
/// Rendered with every JS call answered by default, which is enough to get through boot. The load
/// under test is the one a swapped fetcher starts, because that is the load reachable without
/// Monaco: the boot-time one waits for the editors to initialise.
/// </remarks>
public class IdeDisposeTests
{
    /// <summary>Holds the introspection until released, and records whether it was told to stop.</summary>
    sealed class HeldFetcher :
        IGraphQLFetcher
    {
        readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public bool Cancelled { get; private set; }

        public void Release() =>
            release.TrySetResult();

        public async IAsyncEnumerable<JsonElement> FetchAsync(
            GraphQLRequest request,
            IReadOnlyDictionary<string, string> headers,
            [EnumeratorCancellation] Cancel cancel)
        {
            await using var registration = cancel.Register(() => Cancelled = true);
            started.TrySetResult();
            await release.Task;
            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json), cancel));
            yield return document.RootElement.Clone();
        }
    }

    sealed class NeverCalledFetcher :
        IGraphQLFetcher
    {
        public IAsyncEnumerable<JsonElement> FetchAsync(GraphQLRequest request, IReadOnlyDictionary<string, string> headers, Cancel cancel) =>
            throw new InvalidOperationException("The first fetcher is replaced before anything loads.");
    }

    [Test]
    public async Task DisposingCancelsTheSchemaLoadAndDisownsItsResult()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/BlazorQL/blazorql.js");
        // Never answered: the editors stay uninitialised, which is all of Monaco this needs kept out.
        context.JSInterop.SetupVoid(_ => _.Identifier == "blazorMonaco.editor.create");

        var loaded = 0;
        var cut = context.Render<BlazorQLIde>(_ => _
            .Add(ide => ide.Fetcher, new NeverCalledFetcher())
            .Add(ide => ide.OnSchemaLoaded, () => loaded++));

        // A new fetcher once boot is through re-introspects through it.
        var held = new HeldFetcher();
        cut.Render(_ => _.Add(ide => ide.Fetcher, held));
        await held.Started.WaitAsync(TimeSpan.FromSeconds(10));

        await cut.Instance.DisposeAsync();
        held.Release();
        // Long enough for a result that was still owned to be parsed and installed.
        await Task.Delay(500);

        await Assert.That(held.Cancelled).IsTrue();
        await Assert.That(loaded).IsEqualTo(0);
        await Assert.That(cut.Instance.Schema).IsNull();
    }
}
