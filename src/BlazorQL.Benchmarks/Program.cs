using BenchmarkDotNet.Running;

// Every benchmark in the assembly, filterable from the command line:
//   dotnet run -c Release --project src/BlazorQL.Benchmarks -- --filter *Validate*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// The entry point above is top-level; this exists only to name the assembly for the switcher.
public partial class Program;
