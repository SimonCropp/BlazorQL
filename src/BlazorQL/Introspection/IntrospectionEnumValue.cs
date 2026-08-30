namespace BlazorQL;

/// <summary>One value of an enum type from an introspection result.</summary>
public sealed record IntrospectionEnumValue
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsDeprecated { get; init; }
    public string? DeprecationReason { get; init; }
}
