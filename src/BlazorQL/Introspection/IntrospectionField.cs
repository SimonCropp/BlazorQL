namespace BlazorQL;

/// <summary>One field of an object or interface type from an introspection result.</summary>
public sealed record IntrospectionField
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public IReadOnlyList<IntrospectionInputValue> Args { get; init; } = [];
    public TypeRef Type { get; init; } = new();
    public bool IsDeprecated { get; init; }
    public string? DeprecationReason { get; init; }
}
