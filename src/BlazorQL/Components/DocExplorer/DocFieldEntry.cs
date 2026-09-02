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