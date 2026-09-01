/// <summary>
/// A real GraphQL endpoint over the GraphiQL test schema, for the IDE to talk to. This is the half
/// of the loop the WebAssembly sample cannot provide: its schema runs in the browser, so nothing
/// there exercises browser to http to server and back.
/// </summary>
public static partial class SampleSchemaServer
{
    static readonly BlazorQL.Sample.SampleSchema schema = CreateSchema();
    static readonly DocumentExecuter executer = new();
    static readonly GraphQLSerializer serializer = new();

    static BlazorQL.Sample.SampleSchema CreateSchema()
    {
        var created = new BlazorQL.Sample.SampleSchema();
        created.Initialize();
        return created;
    }

    [GeneratedRegex(@"locations\s+args\(includeDeprecated: true\)")]
    private static partial Regex DirectiveArgsPattern();

    public static void MapSampleSchema(this WebApplication app, string pattern = "/graphql") =>
        app.MapPost(pattern, Handle);

    static async Task Handle(HttpContext context)
    {
        var request = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        var query = request.GetProperty("query").GetString() ?? "";

        // The same two GraphQL.NET introspection gaps the sample's in-browser fetcher patches
        // around, for the same reason: the IDE sends the full spec introspection query, and
        // GraphQL.NET 8 rejects specifiedByURL and includeDeprecated on directive arguments.
        // Neither costs this schema anything. A server that implements the whole spec needs none of
        // this - the workaround belongs to the test server, not to the IDE.
        if (query.Contains("query IntrospectionQuery"))
        {
            query = query.Replace("specifiedByURL", "");
            query = DirectiveArgsPattern().Replace(query, "locations args");
        }

        var result = await executer.ExecuteAsync(
            _ =>
            {
                _.Schema = schema;
                _.Query = query;
                _.ThrowOnUnhandledException = false;
                if (request.TryGetProperty("operationName", out var operation) &&
                    operation.ValueKind == JsonValueKind.String)
                {
                    _.OperationName = operation.GetString();
                }

                if (request.TryGetProperty("variables", out var variables) &&
                    variables.ValueKind == JsonValueKind.Object)
                {
                    _.Variables = serializer.ReadNode<Inputs>(variables);
                }
            });

        context.Response.ContentType = "application/json";
        await serializer.WriteAsync(context.Response.Body, result);
    }
}
