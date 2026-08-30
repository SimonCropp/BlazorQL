namespace BlazorQL;

/// <summary>
/// The single JS-to-C# callback hub the host module invokes. One instance per
/// <see cref="BlazorQLIde"/>, handed to <c>init</c> as a DotNetObjectReference.
/// </summary>
public sealed class BlazorQLCallbacks
{
    public event Action<string, string>? EditorChanged;
    public event Action<string>? EditorAction;
    public event Action<string, string>? StreamNext;
    public event Action<string>? StreamComplete;
    public event Action<string, string>? StreamError;
    public event Action<string, double, double>? PaneResize;
    public event Action<string>? SchemaReference;

    [JSInvokable]
    public void OnEditorChanged(string uriName, string text) =>
        EditorChanged?.Invoke(uriName, text);

    [JSInvokable]
    public void OnEditorAction(string actionId) =>
        EditorAction?.Invoke(actionId);

    [JSInvokable]
    public void OnStreamNext(string streamId, string resultJson) =>
        StreamNext?.Invoke(streamId, resultJson);

    [JSInvokable]
    public void OnStreamComplete(string streamId) =>
        StreamComplete?.Invoke(streamId);

    [JSInvokable]
    public void OnStreamError(string streamId, string message) =>
        StreamError?.Invoke(streamId, message);

    [JSInvokable]
    public void OnPaneResize(string resizerId, double fraction, double size) =>
        PaneResize?.Invoke(resizerId, fraction, size);

    [JSInvokable]
    public void OnSchemaReference(string referenceJson) =>
        SchemaReference?.Invoke(referenceJson);
}
