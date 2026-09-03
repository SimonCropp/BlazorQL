/// <summary>
/// What one response document costs on its way to the screen: accumulated by the merger, rendered
/// as indented JSON, and scanned for the errors the response-error list shows. A subscription pays
/// all of it per event, and the error scan used to be paid per render as well.
/// </summary>
[MemoryDiagnoser]
public class ResponseBenchmarks
{
    JsonDocument document = null!;
    JsonElement payload;
    string rendered = "";

    /// <summary>How many objects the response's list carries — a small page, then a large one.</summary>
    [Params(10, 1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder("""{"data":{"people":[""");
        for (var index = 0; index < Rows; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(
                $$"""{"id":"{{index}}","name":"Person {{index}}","email":"person{{index}}@example.test","tags":["a","b","c"]}""");
        }

        builder.Append("""]},"errors":[{"message":"boom","path":["people",0,"email"]}]}""");
        document = JsonDocument.Parse(builder.ToString());
        payload = document.RootElement;

        var merger = new IncrementalMerger();
        merger.Add(payload);
        rendered = merger.Render();
    }

    [GlobalCleanup]
    public void Cleanup() =>
        document.Dispose();

    /// <summary>One payload in, one indented document out — a subscription event, or a plain result.</summary>
    [Benchmark]
    public int MergeAndRender()
    {
        var merger = new IncrementalMerger();
        merger.Add(payload);
        return merger.Render().Length;
    }

    /// <summary>
    /// A subscription: one merger, one document rendered per event. The shape the response pane
    /// actually sees, and the one a per-render reparse used to multiply.
    /// </summary>
    [Benchmark]
    public int StreamTenEvents()
    {
        var merger = new IncrementalMerger();
        var length = 0;
        for (var index = 0; index < 10; index++)
        {
            merger.Add(payload);
            length += merger.Render().Length;
        }

        return length;
    }

    /// <summary>What the response-error list asks of the response text.</summary>
    [Benchmark]
    public int ParseErrors() =>
        ResponseErrors.Parse(rendered).Count;
}
