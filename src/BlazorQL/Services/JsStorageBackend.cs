/// <summary>
/// The browser storage backend: localStorage through the host module's synchronous exports.
/// Only constructed after the module import completes, so the sync interop is always valid.
/// </summary>
sealed class JsStorageBackend(JsModule module) :
    IStorageBackend
{
    public string? Get(string key) =>
        module.InvokeSync<string?>("storageGet", key);

    public bool Set(string key, string value)
    {
        // storageSet reports quota/privacy failures as {ok, error} rather than throwing across
        // the interop boundary.
        var resultJson = module.InvokeSync<string>("storageSet", key, value);
        using var document = JsonDocument.Parse(resultJson);
        return document.RootElement.GetProperty("ok").GetBoolean();
    }

    public void Remove(string key) =>
        module.InvokeSync("storageRemove", key);

    public IReadOnlyList<string> Keys() =>
        module.InvokeSync<string[]>("storageKeys", "");
}
