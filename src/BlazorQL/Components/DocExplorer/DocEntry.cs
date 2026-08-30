/// <summary>One entry of the documentation explorer's navigation stack.</summary>
abstract class DocEntry
{
    public abstract string Title { get; }

    /// <summary>True when the other entry shows the same page — push de-dupes on this.</summary>
    public abstract bool SameAs(DocEntry other);
}

/// <summary>The root "Docs" page.</summary>
sealed class DocRootEntry :
    DocEntry
{
    public override string Title => "Docs";

    public override bool SameAs(DocEntry other) =>
        other is DocRootEntry;
}

/// <summary>A type page.</summary>
sealed class DocTypeEntry(IntrospectionType type) :
    DocEntry
{
    public IntrospectionType Type => type;

    public override string Title => type.Name;

    public override bool SameAs(DocEntry other) =>
        other is DocTypeEntry entry &&
        entry.Type.Name == type.Name;
}

/// <summary>A field page — a field or input-object field, addressed by name on its parent.</summary>
sealed class DocFieldEntry(IntrospectionType parent, string name) :
    DocEntry
{
    public IntrospectionType Parent => parent;
    public string Name => name;

    public override string Title => name;

    public override bool SameAs(DocEntry other) =>
        other is DocFieldEntry entry &&
        entry.Parent.Name == parent.Name &&
        entry.Name == name;
}
