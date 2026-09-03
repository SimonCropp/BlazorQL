/// <summary>
/// The work a keystroke sets off: the whole document validated, and completion or hover resolved at
/// the caret. Measured against a schema whose types carry hundreds of members, because that is
/// where a lookup's shape shows and a small schema hides it.
/// </summary>
[MemoryDiagnoser]
public class LanguageBenchmarks
{
    static readonly SchemaIndex wide = Schemas.Wide;

    /// <summary>A selection of 100 fields on a 200-field type, which is an ordinary editing session.</summary>
    static readonly string document = BuildDocument();

    static readonly DocumentInfo parsed = DocumentInfo.Parse(document);
    static readonly SchemaValidator validator = new(wide);

    static string BuildDocument()
    {
        var builder = new StringBuilder("query Q($id: String) {\n  root0(id: $id) {\n");
        for (var index = 0; index < 100; index++)
        {
            builder.Append($"    field{index}\n");
        }

        builder.Append("  }\n}");
        return builder.ToString();
    }

    [Benchmark]
    public int Validate() =>
        validator.Validate(parsed).Count;

    [Benchmark]
    public int CompleteAField() =>
        // The caret sits inside the selection set, where completion offers the type's fields.
        CompletionEngine.Complete(wide, document, document.IndexOf("field50", StringComparison.Ordinal)).Count;

    [Benchmark]
    public HoverInfo? HoverAField() =>
        HoverEngine.Hover(wide, document, document.IndexOf("field50", StringComparison.Ordinal) + 2);

    [Benchmark]
    public string FillLeafs() =>
        LeafFiller.Fill(wide, "{ root0(id: \"a\") }").Result;
}
