namespace BlazorQL;

/// <summary>
/// An argument or input-object field from an introspection result. <see cref="DefaultValue"/> is
/// the GraphQL-literal rendering the server produced, ready to display as-is.
/// </summary>
public sealed record IntrospectionInputValue
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public TypeRef Type { get; init; } = new();
    public string? DefaultValue { get; init; }
    public bool IsDeprecated { get; init; }
    public string? DeprecationReason { get; init; }
}
