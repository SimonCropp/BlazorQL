namespace BlazorQL;

/// <summary>
/// A (possibly wrapped) type reference from an introspection result. NON_NULL and LIST wrappers
/// nest through <see cref="OfType"/>; only the innermost reference carries a name.
/// </summary>
public sealed record TypeRef
{
    public string Kind { get; init; } = "";
    public string? Name { get; init; }
    public TypeRef? OfType { get; init; }

    /// <summary>The innermost named type this reference wraps.</summary>
    public TypeRef Unwrap() =>
        OfType?.Unwrap() ?? this;

    /// <summary>Renders the full nesting, e.g. <c>[Foo!]!</c>.</summary>
    public string Display() =>
        Kind switch
        {
            "NON_NULL" => $"{OfType?.Display()}!",
            "LIST" => $"[{OfType?.Display()}]",
            _ => Name ?? ""
        };
}
