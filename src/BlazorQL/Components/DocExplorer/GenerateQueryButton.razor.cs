namespace BlazorQL;

/// <summary>The icon button that asks for a generated query over a type — one per type row on the
/// root page, one in the header of a type page.</summary>
public partial class GenerateQueryButton
{
    [Parameter]
    [EditorRequired]
    public IntrospectionType Type { get; set; } = null!;

    [Parameter]
    public EventCallback<IntrospectionType> OnClick { get; set; }
}
