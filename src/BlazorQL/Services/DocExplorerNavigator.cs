namespace BlazorQL;

/// <summary>
/// Carries jump-to-doc navigation from the IDE into the documentation explorer. When the explorer
/// is mounted its subscription handles the reference immediately; otherwise the reference is held
/// until the explorer mounts and collects it.
/// </summary>
public sealed class DocExplorerNavigator
{
    public event Action<SchemaReference>? Navigated;

    /// <summary>A reference that arrived while no explorer was mounted.</summary>
    public SchemaReference? Pending { get; private set; }

    public void NavigateTo(SchemaReference reference)
    {
        var handler = Navigated;
        if (handler is null)
        {
            Pending = reference;
            return;
        }

        handler(reference);
    }

    /// <summary>Collects (and clears) the reference held for a newly mounted explorer.</summary>
    public SchemaReference? TakePending()
    {
        var pending = Pending;
        Pending = null;
        return pending;
    }
}
