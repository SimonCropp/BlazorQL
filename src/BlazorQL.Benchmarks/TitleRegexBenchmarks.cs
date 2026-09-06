/// <summary>
/// A tab's title, by document shape. <see cref="DerivedTextBenchmarks"/> varies how many rows ask
/// for one; this varies what they are asking about, which is where the regex behind it behaves
/// differently. The cost is not in the document that matches — it is in the one that does not, where
/// <c>.*</c> takes a whole line and then gives it back a character at a time looking for the
/// keyword, once per line.
/// </summary>
[MemoryDiagnoser]
public class TitleRegexBenchmarks
{
    TabState tab = null!;

    /// <summary>
    /// shorthand: the anonymous document a fresh tab starts with. named: a match on the first line.
    /// anonymous: a long document with none of the three keywords in it. mentions: the same, but
    /// with a comment that says "query" — enough to keep any shortcut from taking.
    /// </summary>
    [Params("shorthand", "named", "anonymous", "mentions")]
    public string Shape { get; set; } = "";

    [GlobalSetup]
    public void Setup() =>
        tab = new()
        {
            Query = Shape switch
            {
                "shorthand" => "{\n  id\n  isTest\n}",
                "named" => Long("query GetEverything {\n"),
                "mentions" => Long("# the query below is anonymous\n{\n"),
                _ => Long("{\n")
            }
        };

    static string Long(string opening)
    {
        var builder = new StringBuilder(opening);
        for (var index = 0; index < 200; index++)
        {
            builder.Append($"  field{index}\n");
        }

        builder.Append('}');
        return builder.ToString();
    }

    [Benchmark]
    public string Title() =>
        TabStore.Title(tab);
}
