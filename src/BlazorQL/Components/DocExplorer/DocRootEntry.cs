/// <summary>The root "Docs" page.</summary>
sealed class DocRootEntry :
    DocEntry
{
    public override string Title => "Docs";

    public override bool SameAs(DocEntry other) =>
        other is DocRootEntry;
}