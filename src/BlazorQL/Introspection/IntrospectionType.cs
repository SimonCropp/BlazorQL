namespace BlazorQL;

/// <summary>
/// One named type from an introspection result. The member lists are null when the kind has no
/// such members (e.g. scalars have no fields), matching the wire shape.
/// </summary>
public sealed record IntrospectionType
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? SpecifiedByURL { get; init; }
    public IReadOnlyList<IntrospectionField>? Fields { get; init; }
    public IReadOnlyList<IntrospectionInputValue>? InputFields { get; init; }
    public IReadOnlyList<TypeRef>? Interfaces { get; init; }
    public IReadOnlyList<IntrospectionEnumValue>? EnumValues { get; init; }
    public IReadOnlyList<TypeRef>? PossibleTypes { get; init; }
}
