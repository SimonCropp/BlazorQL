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
        // Resolved per page rather than per render: OnParametersSet runs on every render of the
        // explorer, and on a type with hundreds of fields these are two scans of all of them.
        var key = $"{Parent.Name}.{FieldName}";
        if (lastKey == key)
        {
            return;
        }

        lastKey = key;
        showDeprecatedArgs = false;
        field = Parent.Fields?.FirstOrDefault(_ => _.Name == FieldName);
        inputField = field is null
            ? Parent.InputFields?.FirstOrDefault(_ => _.Name == FieldName)
            : null;
    }
}
