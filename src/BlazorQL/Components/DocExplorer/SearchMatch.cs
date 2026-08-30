/// <summary>
/// One documentation-search hit: a type (only <see cref="Type"/> set), a field on a type, or an
/// argument on a field.
/// </summary>
sealed record SearchMatch(
    IntrospectionType Type,
    string? FieldName = null,
    TypeRef? FieldType = null,
    string? ArgumentName = null,
    TypeRef? ArgumentType = null);
