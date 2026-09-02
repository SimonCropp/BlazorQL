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