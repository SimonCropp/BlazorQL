# Benchmarks

BenchmarkDotNet over the code paths the IDE runs on a keystroke, a render or a response — the ones
where a cost is paid over and over rather than once.

```
dotnet run -c Release --project src/BlazorQL.Benchmarks -- --filter *
```

`--filter` takes a pattern (`*Validate*`), `--job short` trades precision for a faster answer, and
`--list flat` prints what there is to run.

The schemas the benchmarks measure against live in `Schemas.cs`: the sample schema as its own
introspection result, which is exactly the shape the IDE has at runtime, and a synthetic wide one,
because the costs that scale with member counts do not show up on a schema as small as the sample.
