namespace BlazorQL;

/// <summary>
/// The single JS-to-C# callback hub the host module invokes. One instance per
/// <see cref="BlazorQLIde"/>, handed to <c>init</c> as a DotNetObjectReference.
/// </summary>
public sealed class BlazorQLCallbacks
{
    public event Action<string, double, double>? PaneResize;
    public event Action<string>? GlobalShortcut;

    [JSInvokable]
    public void OnPaneResize(string resizerId, double fraction, double size) =>
        PaneResize?.Invoke(resizerId, fraction, size);

    [JSInvokable]
    public void OnGlobalShortcut(string id) =>
        GlobalShortcut?.Invoke(id);
}
