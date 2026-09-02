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

    IEnumerable<IntrospectionType> OtherTypes() =>
        Schema.Types
            .Where(_ => !_.Name.StartsWith("__", StringComparison.Ordinal) && !Schema.IsRootType(_.Name))
            .OrderBy(_ => _.Name, StringComparer.Ordinal);
}
