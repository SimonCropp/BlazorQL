namespace BlazorQL;

/// <summary>
/// The lazily imported <c>blazorql.js</c> host module — pure browser utilities (clipboard,
/// downloads, storage, shortcuts, pane dragging). One instance per <see cref="BlazorQLIde"/>.
/// </summary>
public sealed class JsModule(IJSRuntime js) :
    IAsyncDisposable
{
    const DynamicallyAccessedMemberTypes interopMembers =
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties;

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

    /// <remarks>
    /// T is only ever a primitive or a string array here — what blazorql.js hands back. The
    /// annotation forwards the interop contract so a caller with a richer type is the one told about
    /// it, rather than the failure surfacing as an empty object in a trimmed build.
    /// </remarks>
    public async ValueTask<T> Invoke<[DynamicallyAccessedMembers(interopMembers)] T>(string identifier, params object?[] args)
    {
        var target = await Get();
        return await target.InvokeAsync<T>(identifier, args);
    }

    /// <summary>
    /// Synchronous invoke, for the storage backend's synchronous contract. Only valid after the
    /// module is imported and only on an in-process (WebAssembly) runtime.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "T is a primitive or a string array, which the interop serializer handles without reflection.")]
    public T InvokeSync<[DynamicallyAccessedMembers(interopMembers)] T>(string identifier, params object?[] args)
    {
        if (module is not IJSInProcessObjectReference inProcess)
        {
            throw new InvalidOperationException("The host module is not loaded, or the JS runtime is not in-process.");
        }

        return inProcess.Invoke<T>(identifier, args);
    }

    /// <summary>Synchronous void invoke — see <see cref="InvokeSync{T}"/>.</summary>
    public void InvokeSync(string identifier, params object?[] args)
    {
        if (module is not IJSInProcessObjectReference inProcess)
        {
            throw new InvalidOperationException("The host module is not loaded, or the JS runtime is not in-process.");
        }

        inProcess.InvokeVoid(identifier, args);
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
