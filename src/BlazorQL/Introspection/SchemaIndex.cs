namespace BlazorQL;

/// <summary>
/// A parsed introspection result with name-to-type lookup — what the documentation explorer
/// navigates over.
/// </summary>
public sealed class SchemaIndex
{
    readonly Dictionary<string, IntrospectionType> byName;

    SchemaIndex(IntrospectionSchema schema)
    {
        Schema = schema;
        byName = new(StringComparer.Ordinal);
        foreach (var type in schema.Types)
        {
            byName[type.Name] = type;
        }
    }

    public IntrospectionSchema Schema { get; }

    public string? Description => Schema.Description;
    public IReadOnlyList<IntrospectionType> Types => Schema.Types;
    public IReadOnlyList<IntrospectionDirective> Directives => Schema.Directives;
    public string? QueryTypeName => Schema.QueryType?.Name;
    public string? MutationTypeName => Schema.MutationType?.Name;
    public string? SubscriptionTypeName => Schema.SubscriptionType?.Name;

    /// <summary>True when the name is one of the schema's root operation types.</summary>
    public bool IsRootType(string name) =>
        name == QueryTypeName ||
        name == MutationTypeName ||
        name == SubscriptionTypeName;

    public IntrospectionType? Find(string? name) =>
        name is not null && byName.TryGetValue(name, out var type)
            ? type
            : null;

    /// <summary>
    /// Parses a standard introspection result. Accepts the <c>__schema</c> object wrapped in
    /// <c>data</c> (a full response) or at the root (a bare result). Null when neither shape fits.
    /// </summary>
    public static SchemaIndex? Parse(JsonElement introspection)
    {
        var root = introspection;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object)
        {
            root = data;
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("__schema", out var schemaElement) ||
            schemaElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var schema = schemaElement.Deserialize(WebJson.Default.IntrospectionSchema);
        if (schema is null)
        {
            // An empty schema is indistinguishable from a server with nothing to say, so it is
            // worth a word in the console rather than nothing at all.
            Console.Error.WriteLine("BlazorQL: the introspection result deserialized to nothing.");
            return null;
        }

        return new(schema);
    }
}
