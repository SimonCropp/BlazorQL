namespace BlazorQL;

/// <summary>
/// The pane the sidebar toggles open: a titled host for whichever plugin <see cref="Kind"/>
/// selects — the documentation explorer, the execution history, or the query builder.
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

    /// <summary>
    /// Asked for the schema as SDL, for the documentation explorer's SDL view. A function rather
    /// than the text, so printing a large schema is not paid for a view nobody opened.
    /// </summary>
    [Parameter]
    public Func<string?>? SdlProvider { get; set; }

    /// <summary>Carries jump-to-doc navigation into the documentation explorer.</summary>
    [Parameter]
    public DocExplorerNavigator? Navigator { get; set; }

    /// <summary>Raised with a document the documentation explorer generated — the parent loads it
    /// into a tab.</summary>
    [Parameter]
    public EventCallback<string> OnGenerateQuery { get; set; }

    [Parameter]
    public EventCallback<string> OnCopyGenerated { get; set; }

    /// <summary>The execution history rendered when <see cref="Kind"/> is History.</summary>
    [Parameter]
    public HistoryStore? History { get; set; }

    /// <summary>Raised when a history item is picked — the parent loads it into the editors.</summary>
    [Parameter]
    public EventCallback<HistoryItem> OnHistorySelect { get; set; }

    /// <summary>The operation editor's text, which the query builder renders its tree from.</summary>
    [Parameter]
    public string Query { get; set; } = "";

    /// <summary>Raised with an edit the query builder made — the parent applies it to the operation editor.</summary>
    [Parameter]
    public EventCallback<Func<string, string?>> OnBuilderEdit { get; set; }

    /// <summary>Raised with the edit to the variables document that keeps it in step with a builder edit.</summary>
    [Parameter]
    public EventCallback<Func<string, string?>> OnBuilderVariablesEdit { get; set; }

    string Title =>
        Kind switch
        {
            PluginKind.Docs => "Documentation Explorer",
            PluginKind.Builder => "Query Builder",
            _ => "History"
        };
}
