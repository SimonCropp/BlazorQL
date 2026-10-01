namespace BlazorQL;

/// <summary>
/// One step on the way down from a definition's root selection set: a field by name, or an inline
/// fragment by the type it is conditioned on.
/// </summary>
/// <remarks>
/// The query builder addresses what it edits by the steps that lead to it rather than by a node,
/// because every edit parses the text afresh — the editor may have moved on since the pane last
/// rendered, and a node from that render would point at offsets that now hold something else.
/// </remarks>
public readonly record struct BuilderStep(string Name, bool IsFragment)
{
    public static BuilderStep Field(string name) =>
        new(name, false);

    public static BuilderStep Fragment(string typeName) =>
        new(typeName, true);
}
