namespace BlazorQL;

/// <summary>
/// The Variables/Headers strip under the operation editor. The editor host elements are always in
/// the DOM — the two Monaco editors are created once at boot and shown/hidden with CSS so their
/// undo/scroll state survives tab flips and collapses.
/// </summary>
public partial class EditorTools
{
    internal const string VariablesElementId = "blazorql-variables-editor";
    internal const string HeadersElementId = "blazorql-headers-editor";

    StandaloneCodeEditor? variablesEditor;
    StandaloneCodeEditor? headersEditor;

    /// <summary>The variables editor, once its component has rendered.</summary>
    public StandaloneCodeEditor? VariablesEditor => variablesEditor;

    /// <summary>The headers editor, once its component has rendered. Null when headers are disabled.</summary>
    public StandaloneCodeEditor? HeadersEditor => headersEditor;

    [Parameter]
    public bool Expanded { get; set; }

    [Parameter]
    public EditorTool ActiveTool { get; set; }

    [Parameter]
    public bool HeadersEnabled { get; set; } = true;

    /// <summary>Inline flex sizing supplied by the parent's pane state.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>
    /// Construction options for the variables editor. Null (pre-hydration) renders no editor —
    /// the parent supplies these only once persisted state is available to seed the value from.
    /// </summary>
    [Parameter]
    public Func<StandaloneCodeEditor, StandaloneEditorConstructionOptions>? VariablesConstruction { get; set; }

    /// <summary>Construction options for the headers editor — see <see cref="VariablesConstruction"/>.</summary>
    [Parameter]
    public Func<StandaloneCodeEditor, StandaloneEditorConstructionOptions>? HeadersConstruction { get; set; }

    [Parameter]
    public EventCallback OnVariablesInit { get; set; }

    [Parameter]
    public EventCallback OnHeadersInit { get; set; }

    [Parameter]
    public EventCallback<ModelContentChangedEvent> OnVariablesChanged { get; set; }

    [Parameter]
    public EventCallback<ModelContentChangedEvent> OnHeadersChanged { get; set; }

    [Parameter]
    public EventCallback<EditorTool> OnSelectTool { get; set; }

    [Parameter]
    public EventCallback OnToggle { get; set; }
}
