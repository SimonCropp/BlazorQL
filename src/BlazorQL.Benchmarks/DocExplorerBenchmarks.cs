/// <summary>
/// The documentation explorer's root page, which lists every type the schema declares and asks two
/// questions about each. It renders far more often than it changes — every pointer move during a
/// pane drag re-renders it — so what one render costs is what matters.
/// </summary>
[MemoryDiagnoser]
public class DocExplorerBenchmarks
{
    static readonly SchemaIndex wide = Schemas.Wide;

    [Benchmark]
    public int RootPageRows()
    {
        var rows = 0;
        foreach (var type in wide.Types
                     .Where(_ => !_.Name.StartsWith("__", StringComparison.Ordinal) && !wide.IsRootType(_.Name))
                     .OrderBy(_ => _.Name, StringComparer.Ordinal))
        {
            if (QueryGenerator.CanGenerate(type))
            {
                rows++;
            }

            if (QueryGenerator.CanGenerateOperation(wide, type))
            {
                rows++;
            }
        }

        return rows;
    }
}
