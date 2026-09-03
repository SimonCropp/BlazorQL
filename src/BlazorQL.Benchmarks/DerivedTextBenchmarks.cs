/// <summary>
/// Text derived per row on every render: a tab's title, and a history item's one-line form. Neither
/// is expensive on its own; both are asked for once per row on every render of the IDE, and a pane
/// drag renders at frame rate.
/// </summary>
[MemoryDiagnoser]
public class DerivedTextBenchmarks
{
    TabState[] tabs = null!;
    HistoryItem[] items = null!;

    /// <summary>Rows on screen — a busy session's tab strip, and a full history pane.</summary>
    [Params(5, 20)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var query = BuildQuery();
        tabs = [.. Enumerable.Range(0, Rows).Select(_ => new TabState {Query = query})];
        items = [.. Enumerable.Range(0, Rows).Select(_ => new HistoryItem {Query = query})];
    }

    static string BuildQuery()
    {
        var builder = new StringBuilder("# a comment about the query\n");
        for (var index = 0; index < 200; index++)
        {
            builder.Append($"  field{index}\n");
        }

        return builder.ToString();
    }

    [Benchmark]
    public int TabTitles()
    {
        var length = 0;
        foreach (var tab in tabs)
        {
            length += TabStore.Title(tab).Length;
        }

        return length;
    }

    [Benchmark]
    public int HistoryDisplayText()
    {
        var length = 0;
        foreach (var item in items)
        {
            length += HistoryStore.DisplayText(item).Length;
        }

        return length;
    }
}
