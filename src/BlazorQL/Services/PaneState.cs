namespace BlazorQL;

/// <summary>
/// One resizable pane pair's split ratio: the first pane's share of the container, 0..1. Kept as a
/// class so M6 can persist and rehydrate it.
/// </summary>
public sealed class PaneState(double defaultRatio)
{
    public double DefaultRatio { get; } = defaultRatio;

    public double Ratio { get; set; } = defaultRatio;

    /// <summary>Back to the default split — what double-clicking the drag bar does.</summary>
    public void Reset() =>
        Ratio = DefaultRatio;
}
