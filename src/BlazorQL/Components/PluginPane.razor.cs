namespace BlazorQL;

/// <summary>
/// The pane the sidebar toggles open: a titled host for whichever plugin <see cref="Kind"/>
/// selects — the documentation explorer or the execution history.
/// </summary>
public partial class PluginPane
{
    [Parameter]
    public PluginKind Kind { get; set; }

    /// <summary>Inline flex sizing supplied by the parent's pane state.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>The parsed schema for the documentation explorer. Null until introspection lands.</summary>
    [Parameter]
    public SchemaIndex? Schema { get; set; }

    /// <summary>The schema printed as SDL, for the documentation explorer's SDL view.</summary>
    [Parameter]
    public string? SchemaSdl { get; set; }

    /// <summary>Carries jump-to-doc navigation into the documentation explorer.</summary>
    [Parameter]
    public DocExplorerNavigator? Navigator { get; set; }

    /// <summary>Raised with a document the documentation explorer generated — the parent loads it
    /// into a tab.</summary>
    [Parameter]
    public EventCallback<string> OnGenerateQuery { get; set; }

    /// <summary>The execution history rendered when <see cref="Kind"/> is History.</summary>
    [Parameter]
    public HistoryStore? History { get; set; }

    /// <summary>Raised when a history item is picked — the parent loads it into the editors.</summary>
    [Parameter]
    public EventCallback<HistoryItem> OnHistorySelect { get; set; }

    string Title =>
        Kind == PluginKind.Docs
            ? "Documentation Explorer"
            : "History";
}
