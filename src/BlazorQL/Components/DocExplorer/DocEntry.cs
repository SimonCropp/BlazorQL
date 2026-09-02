/// <summary>One entry of the documentation explorer's navigation stack.</summary>
abstract class DocEntry
{
    public abstract string Title { get; }

    /// <summary>True when the other entry shows the same page — push de-dupes on this.</summary>
    public abstract bool SameAs(DocEntry other);
}