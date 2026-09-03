namespace BlazorQL;

/// <summary>
/// The documentation explorer's root page: schema description, root operation types, and every
/// other type the schema declares.
/// </summary>
public partial class SchemaDoc
{
    [Parameter]
    [EditorRequired]
    public SchemaIndex Schema { get; set; } = null!;

    [Parameter]
    public EventCallback<string> OnNavigateType { get; set; }

    /// <summary>Raised with the type whose generate-query button was clicked.</summary>
    [Parameter]
    public EventCallback<IntrospectionType> OnGenerateQuery { get; set; }

    [Parameter]
    public EventCallback<IntrospectionType> OnCopyGenerated { get; set; }

    IEnumerable<(string Operation, string TypeName)> RootTypes()
    {
        if (Schema.QueryTypeName is not null)
        {
            yield return ("query", Schema.QueryTypeName);
        }

        if (Schema.MutationTypeName is not null)
        {
            yield return ("mutation", Schema.MutationTypeName);
        }

        if (Schema.SubscriptionTypeName is not null)
        {
            yield return ("subscription", Schema.SubscriptionTypeName);
        }
    }

    /// <summary>One row of the type list, with the questions its two buttons ask already answered.</summary>
    readonly record struct TypeEntry(IntrospectionType Type, bool CanGenerate, bool CanGenerateOperation);

    IReadOnlyList<TypeEntry> otherTypes = [];
    SchemaIndex? lastSchema;

    /// <summary>
    /// The list is built once per schema rather than once per render. Sorting every type and asking
    /// twice per type whether an operation can be generated is work the markup used to redo on each
    /// render, and a pane drag renders at pointer-event rate.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(lastSchema, Schema))
        {
            return;
        }

        lastSchema = Schema;
        otherTypes =
        [
            .. Schema.Types
                .Where(_ => !_.Name.StartsWith("__", StringComparison.Ordinal) && !Schema.IsRootType(_.Name))
                .OrderBy(_ => _.Name, StringComparer.Ordinal)
                .Select(_ => new TypeEntry(
                    _,
                    QueryGenerator.CanGenerate(_),
                    QueryGenerator.CanGenerateOperation(Schema, _)))
        ];
    }
}
