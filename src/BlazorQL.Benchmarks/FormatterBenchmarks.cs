/// <summary>
/// The JSONC parse behind the variables and headers editors. It runs on every diagnostics pass and
/// on every run, so what it allocates and what it holds on to both matter.
/// </summary>
[MemoryDiagnoser]
public class FormatterBenchmarks
{
    string variables = null!;

    [Params(1, 100)]
    public int Entries { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder("{\n");
        for (var index = 0; index < Entries; index++)
        {
            builder.Append($"  \"variable{index}\": \"value {index}\"");
            builder.Append(index == Entries - 1 ? "\n" : ",\n");
        }

        builder.Append('}');
        variables = builder.ToString();
    }

    [Benchmark]
    public JsonElement? ParseJsonc() =>
        Formatter.ParseJsonc(variables, "Variables").Value;
}
