/// <summary>
/// Printing the schema as SDL, which the IDE used to do on every schema load whether or not anyone
/// opened the SDL view.
/// </summary>
[MemoryDiagnoser]
public class SdlPrinterBenchmarks
{
    static readonly SchemaIndex wide = Schemas.Wide;
    static readonly SchemaIndex sample = Schemas.Sample;

    [Benchmark]
    public int PrintWideSchema() =>
        SdlPrinter.Print(wide).Length;

    [Benchmark]
    public int PrintSampleSchema() =>
        SdlPrinter.Print(sample).Length;
}
