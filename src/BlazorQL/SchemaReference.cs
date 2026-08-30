namespace BlazorQL;

/// <summary>
/// A flattened pointer into the schema — what jump-to-doc resolves under the caret and the
/// documentation explorer navigates to. <see cref="Kind"/> is <c>Type</c>, <c>Field</c>, or
/// <c>Argument</c>; the names fill in from the left as the kind requires.
/// </summary>
public sealed record SchemaReference(
    string Kind,
    string? TypeName = null,
    string? FieldName = null,
    string? ArgName = null);
