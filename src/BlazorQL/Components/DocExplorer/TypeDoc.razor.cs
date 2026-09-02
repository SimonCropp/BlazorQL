namespace BlazorQL;

/// <summary>
/// A type page: description, implemented interfaces, fields (input fields included), enum values,
/// and implementations / possible types. Deprecated members hide behind a toggle, unless every
/// member of the section is deprecated.
/// </summary>
public partial class TypeDoc
{
    bool showDeprecatedFields;
    bool showDeprecatedValues;
    string? lastTypeName;

    [Parameter]
    [EditorRequired]
    public IntrospectionType Type { get; set; } = null!;

    [Parameter]
    public EventCallback<string> OnNavigateType { get; set; }

    /// <summary>Raised with the field name to open its field page.</summary>
    [Parameter]
    public EventCallback<string> OnNavigateField { get; set; }

    protected override void OnParametersSet()
    {
        // The component instance survives navigating between type pages; the toggles do not.
        if (lastTypeName != Type.Name)
        {
            lastTypeName = Type.Name;
            showDeprecatedFields = false;
            showDeprecatedValues = false;
        }
    }
}
