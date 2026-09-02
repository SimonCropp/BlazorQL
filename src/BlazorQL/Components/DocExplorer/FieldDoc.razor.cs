namespace BlazorQL;

/// <summary>
/// A field page — a field of an object or interface type, or an input-object field. Introspection
/// carries no directive information for fields, so there is no Directives section.
/// </summary>
public partial class FieldDoc
{
    bool showDeprecatedArgs;
    IntrospectionField? field;
    IntrospectionInputValue? inputField;
    string? lastKey;

    [Parameter]
    [EditorRequired]
    public IntrospectionType Parent { get; set; } = null!;

    [Parameter]
    [EditorRequired]
    public string FieldName { get; set; } = "";

    [Parameter]
    public EventCallback<string> OnNavigateType { get; set; }

    protected override void OnParametersSet()
    {
        var key = $"{Parent.Name}.{FieldName}";
        if (lastKey != key)
        {
            lastKey = key;
            showDeprecatedArgs = false;
        }

        field = Parent.Fields?.FirstOrDefault(_ => _.Name == FieldName);
        inputField = field is null
            ? Parent.InputFields?.FirstOrDefault(_ => _.Name == FieldName)
            : null;
    }
}
