namespace BlazorQL;

/// <summary>
/// The icon rail down the left edge: the plugin pane toggles, and the schema re-fetch, theme,
/// short keys and settings actions. Holds no state of its own — everything is a parameter and a
/// callback back to the IDE.
/// </summary>
public partial class Sidebar
{
    /// <summary>Which plugin pane is open, if any. Drives the toggle buttons' active state.</summary>
    [Parameter]
    public PluginKind? Visible { get; set; }

    /// <summary>Raised with the plugin whose button was clicked; the parent toggles.</summary>
    [Parameter]
    public EventCallback<PluginKind> OnToggle { get; set; }

    /// <summary>True while a schema re-fetch is in flight — spins the re-fetch icon.</summary>
    [Parameter]
    public bool Refetching { get; set; }

    [Parameter]
    public EventCallback OnRefetch { get; set; }

    /// <summary>The current theme preference, surfaced in the toggle's accessible name.</summary>
    [Parameter]
    public Theme Theme { get; set; }

    [Parameter]
    public EventCallback OnThemeToggle { get; set; }

    [Parameter]
    public EventCallback OnOpenShortKeys { get; set; }

    [Parameter]
    public EventCallback OnOpenSettings { get; set; }
}
