namespace BlazorQL;

/// <summary>
/// The lazily imported <c>blazorql.js</c> host module — the single seam to the vendored editor
/// stack. One instance per <see cref="BlazorQLIde"/>.
/// </summary>
public sealed class JsModule(IJSRuntime js) :
    IAsyncDisposable
{
    IJSObjectReference? module;

    public async ValueTask<IJSObjectReference> Get()
    {
        module ??= await js.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/BlazorQL/blazorql.js");
        return module;
    }

    public async ValueTask Invoke(string identifier, params object?[] args)
    {
        var target = await Get();
        await target.InvokeVoidAsync(identifier, args);
    }

    public async ValueTask<T> Invoke<T>(string identifier, params object?[] args)
    {
        var target = await Get();
        return await target.InvokeAsync<T>(identifier, args);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("dispose");
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the module with it.
        }
    }
}
