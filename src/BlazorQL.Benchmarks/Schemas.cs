/// <summary>
/// The schemas the benchmarks measure against. The sample one comes through its own introspection
/// query, so it is exactly the shape the IDE has at runtime; the wide one is synthetic, because the
/// costs that scale with member counts do not show up on a schema this small.
/// </summary>
static class Schemas
{
    public static SchemaIndex Sample { get; } = BuildSample();

    /// <summary>200 object types of 200 fields each — a GitHub-sized schema, roughly.</summary>
    public static SchemaIndex Wide { get; } = BuildWide(types: 200, fields: 200);

    static SchemaIndex BuildSample()
    {
        var schema = new BlazorQL.Sample.SampleSchema();
        var result = new DocumentExecuter()
            .ExecuteAsync(
                new()
                {
                    Schema = schema,
                    Query = BlazorQLIde.IntrospectionQuery(draftAdditions: false)
                })
            .GetAwaiter()
            .GetResult();

        using var document = JsonDocument.Parse(new GraphQLSerializer().Serialize(result));
        return SchemaIndex.Parse(document.RootElement.GetProperty("data"))!;
    }

    static SchemaIndex BuildWide(int types, int fields)
    {
        var builder = new StringBuilder();
        builder.Append("""{"__schema":{"queryType":{"name":"Query"},"types":[""");

        builder.Append("""{"kind":"OBJECT","name":"Query","fields":[""");
        for (var type = 0; type < types; type++)
        {
            if (type > 0)
            {
                builder.Append(',');
            }

            builder.Append(
                $$$"""{"name":"root{{{type}}}","isDeprecated":false,"args":[{"name":"id","type":{"kind":"SCALAR","name":"String"},"isDeprecated":false}],"type":{"kind":"OBJECT","name":"Type{{{type}}}"}}""");
        }

        builder.Append("]}");

        for (var type = 0; type < types; type++)
        {
            builder.Append($$""",{"kind":"OBJECT","name":"Type{{type}}","fields":[""");
            for (var field = 0; field < fields; field++)
            {
                if (field > 0)
                {
                    builder.Append(',');
                }

                builder.Append(
                    $$$"""{"name":"field{{{field}}}","isDeprecated":false,"args":[],"type":{"kind":"SCALAR","name":"String"}}""");
            }

            builder.Append("]}");
        }

        builder.Append(""",{"kind":"SCALAR","name":"String"}],"directives":[]}}""");

        using var document = JsonDocument.Parse(builder.ToString());
        return SchemaIndex.Parse(document.RootElement)!;
    }
}
