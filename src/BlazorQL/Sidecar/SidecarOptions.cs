namespace BlazorQL;

/// <summary>
/// Options for the debug sidecar: the <see cref="BlazorQLSidecar"/> panel and the
/// <see cref="SidecarFetcher"/> capture. Configured through
/// <see cref="SidecarServiceExtensions.AddBlazorQLSidecar"/>.
/// </summary>
public sealed class SidecarOptions
{
    // begin-snippet: sidecarOptions
    /// <summary>
    /// Whether requests are captured and the panel responds to its shortcut. On by default —
    /// turn it off for builds where a query log over the GraphQL traffic is unwanted.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The keyboard shortcut that opens and hides the panel, as modifier tokens plus a key
    /// (for example <c>"Ctrl+Shift+D"</c>). An unrecognized value falls back to the default.
    /// </summary>
    public string ToggleShortcut { get; set; } = "Alt+G";

    /// <summary>
    /// Decides whether the small floating button is shown in the page's corner while the panel
    /// is closed, as a clickable alternative to the shortcut. Shown to everyone by default —
    /// set <see cref="Never"/> to rely on the shortcut alone, or an own predicate to decide from
    /// the current context (the signed-in user, say). Evaluated once, when the panel first loads.
    /// </summary>
    public Func<IServiceProvider, ValueTask<bool>> ToggleButton { get; set; } = Always;

    /// <summary>
    /// Where a <see cref="BlazorQLIde"/> is routed, for the "open in BlazorQL" action on a
    /// captured request — the action opens that route with the query and variables carried in a
    /// <c>#q=</c> share fragment. The default empty string targets the current page, which is
    /// right when the sidecar sits beside the IDE itself. Null hides the action.
    /// </summary>
    public string? IdeRoute { get; set; } = "";

    /// <summary>Captured requests kept; the oldest is evicted beyond this.</summary>
    public int MaxEntries { get; set; } = 100;

    /// <summary>
    /// Response documents kept per request. One request can yield many documents — incremental
    /// patches, subscription events — and an unbounded subscription must not grow the log without
    /// end, so documents beyond this are counted but not kept.
    /// </summary>
    public int MaxDocumentsPerEntry { get; set; } = 25;
    // end-snippet

    /// <summary>Shows the toggle button to everyone. The default.</summary>
    public static ValueTask<bool> Always(IServiceProvider services) =>
        ValueTask.FromResult(true);

    /// <summary>Never shows the toggle button — the shortcut is the only way in.</summary>
    public static ValueTask<bool> Never(IServiceProvider services) =>
        ValueTask.FromResult(false);
}
