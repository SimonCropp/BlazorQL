namespace BlazorQL;

/// <summary>
/// The single JS-to-C# callback hub the host module invokes. One instance per
/// <see cref="BlazorQLIde"/>, handed to <c>init</c> as a DotNetObjectReference.
/// </summary>
public sealed class BlazorQLCallbacks
{
    public event Action<string, string>? EditorChanged;

    [JSInvokable]
    public void OnEditorChanged(string uriName, string text) =>
        EditorChanged?.Invoke(uriName, text);
}
