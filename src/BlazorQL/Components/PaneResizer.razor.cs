namespace BlazorQL;

/// <summary>
/// The drag bar between two panes. Always in the DOM (hidden via CSS when its pane is closed) so
/// the host module's pointer tracking is attached exactly once, at boot. Dragging is reported
/// through the callback hub; double-click resets the split.
/// </summary>
public partial class PaneResizer
{
    /// <summary>Element id the host module's trackPointer attaches to.</summary>
    [Parameter]
    [EditorRequired]
    public string Id { get; set; } = "";

    /// <summary>Drag axis: "x" resizes side-by-side panes, "y" stacked panes.</summary>
    [Parameter]
    public string Direction { get; set; } = "x";

    /// <summary>Hides the bar (its pane is closed) without detaching the pointer tracking.</summary>
    [Parameter]
    public bool Hidden { get; set; }

    [Parameter]
    public EventCallback OnReset { get; set; }
}
