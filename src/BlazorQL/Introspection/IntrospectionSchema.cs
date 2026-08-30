namespace BlazorQL;

/// <summary>The <c>__schema</c> object of a standard introspection result.</summary>
public sealed record IntrospectionSchema
{
    public string? Description { get; init; }
    public TypeRef? QueryType { get; init; }
    public TypeRef? MutationType { get; init; }
    public TypeRef? SubscriptionType { get; init; }
    public IReadOnlyList<IntrospectionType> Types { get; init; } = [];
    public IReadOnlyList<IntrospectionDirective> Directives { get; init; } = [];
}
