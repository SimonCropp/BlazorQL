namespace BlazorQL;

/// <summary>One directive definition from an introspection result.</summary>
public sealed record IntrospectionDirective
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public IReadOnlyList<string> Locations { get; init; } = [];
    public IReadOnlyList<IntrospectionInputValue> Args { get; init; } = [];
    public bool IsRepeatable { get; init; }
}
