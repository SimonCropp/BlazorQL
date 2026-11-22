using Microsoft.JSInterop;

/// <summary>
/// The IDE between its first render and the end of boot. The chrome (sidebar, toolbar, tab strip)
/// renders at the first await, while the JS module is still being imported, so it can be clicked
/// before storage, the tab state or the editors exist. A click there threw: a null editor, an
/// empty tab store read during render, or a null store written to.
/// </summary>
/// <remarks>
/// The one place this suite renders <see cref="BlazorQLIde"/> at all, and only because the window
/// is before any editor exists: the module import is held open, so Monaco never comes into it.
/// </remarks>
public class IdeBootWindowTests
{
    sealed class NeverCalledFetcher :
        IGraphQLFetcher
    {
        public IAsyncEnumerable<JsonElement> FetchAsync(GraphQLRequest request, IReadOnlyDictionary<string, string> headers, Cancel cancel) =>
            throw new InvalidOperationException("Nothing should be fetched before boot.");
    }

    static IRenderedComponent<BlazorQLIde> RenderBooting(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        // Never answered: the module's init is boot's first await, the same window an import
        // still in flight leaves.
        context.JSInterop.SetupModule("./_content/BlazorQL/blazorql.js").SetupVoid("init", _ => true);
        return context.Render<BlazorQLIde>(_ => _.Add(ide => ide.Fetcher, new NeverCalledFetcher()));
    }

    [Test]
    [Arguments("sidebar-docs")]
    [Arguments("sidebar-history")]
    [Arguments("sidebar-builder")]
    [Arguments("refetch")]
    [Arguments("theme-toggle")]
    [Arguments("shortkeys")]
    [Arguments("settings")]
    [Arguments("tab-add")]
    [Arguments("tab-duplicate")]
    [Arguments("tab-import")]
    [Arguments("execute")]
    [Arguments("prettify")]
    [Arguments("merge")]
    [Arguments("copy")]
    [Arguments("share")]
    public async Task AControlClickedDuringBootDoesNothing(string testId)
    {
        await using var context = new BunitContext();
        var cut = RenderBooting(context);
        var before = cut.Markup;

        await cut.Find($"[data-testid='{testId}']").ClickAsync(new());

        // Nothing to act on yet, so nothing changes, and above all nothing throws.
        await Assert.That(cut.Markup).IsEqualTo(before);
    }
}
