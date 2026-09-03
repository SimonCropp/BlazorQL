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

    /// <summary>
    /// The shortest chain of fields reaching <paramref name="name"/> from the query root, or null
    /// when nothing within <see cref="maxPathLength"/> hops does.
    /// </summary>
    /// <remarks>
    /// Computed once for the whole schema rather than per ask: the documentation explorer lists
    /// every type, and a search apiece would be quadratic in the size of the schema.
    /// </remarks>
    public IReadOnlyList<IntrospectionField>? PathFromQuery(string name)
    {
        paths ??= BuildPaths();
        return paths.GetValueOrDefault(name);
    }

    /// <summary>
    /// How deep a chain is worth offering. A type further out than this is reachable in principle
    /// and unusable in practice — the query would nest further than anyone wants to read.
    /// </summary>
    const int maxPathLength = 3;

    Dictionary<string, IReadOnlyList<IntrospectionField>>? paths;

    Dictionary<string, IReadOnlyList<IntrospectionField>> BuildPaths()
    {
        var found = new Dictionary<string, IReadOnlyList<IntrospectionField>>(StringComparer.Ordinal);
        var root = Find(QueryTypeName);
        if (root is null)
        {
            return found;
        }

        // Breadth first, so the chain recorded for a type is the shortest that reaches it. The
        // seen set doubles as the cycle guard, which schemas of any size need.
        var queue = new Queue<(IntrospectionType Type, IReadOnlyList<IntrospectionField> Path)>();
        queue.Enqueue((root, []));
        var seen = new HashSet<string>(StringComparer.Ordinal) {root.Name};

        while (queue.Count > 0)
        {
            var (type, path) = queue.Dequeue();
            foreach (var field in type.Fields ?? [])
            {
                if (field.IsDeprecated)
                {
                    continue;
                }

                var target = Find(field.Type.Unwrap().Name);
                if (target is null ||
                    !seen.Add(target.Name))
                {
                    continue;
                }

                IReadOnlyList<IntrospectionField> next = [.. path, field];
                found[target.Name] = next;
                if (next.Count < maxPathLength &&
                    target.Kind is "OBJECT" or "INTERFACE")
                {
                    queue.Enqueue((target, next));
                }
            }
        }

        return found;
    }

    public IntrospectionType? Find(string? name)
    {
        if (name is not null && byName.TryGetValue(name, out var type))
        {
            return type;
        }

        return null;
    }

    /// <summary>
    /// Member lookups, built per type on first ask. The language layer resolves a member by name on
    /// every keystroke — diagnostics walk the whole document, completion and hover walk to the
    /// caret — and a linear scan of a type with hundreds of fields is paid once per selected field
    /// every time. Lazily, because a session touches a handful of types out of a schema's hundreds.
    /// </summary>
    /// <remarks>
    /// Keyed by type name, which is unique in a schema. The types passed in are expected to be this
    /// index's own, as everything reaching them through <see cref="Find"/> is.
    /// </remarks>
    readonly Dictionary<string, Members> members = new(StringComparer.Ordinal);

    sealed class Members
    {
        public Dictionary<string, IntrospectionField>? Fields { get; set; }
        public Dictionary<string, IntrospectionInputValue>? InputFields { get; set; }
        public Dictionary<string, IntrospectionEnumValue>? EnumValues { get; set; }
    }

    Dictionary<string, IntrospectionDirective>? directivesByName;

    /// <summary>The named field of a type, or null. See <see cref="members"/>.</summary>
    public IntrospectionField? Field(IntrospectionType? type, string name)
    {
        if (type is null)
        {
            return null;
        }

        var table = MembersOf(type);
        table.Fields ??= Build(type.Fields, _ => _.Name);
        return table.Fields.GetValueOrDefault(name);
    }

    /// <summary>The named input field of a type, or null. See <see cref="members"/>.</summary>
    public IntrospectionInputValue? InputField(IntrospectionType? type, string name)
    {
        if (type is null)
        {
            return null;
        }

        var table = MembersOf(type);
        table.InputFields ??= Build(type.InputFields, _ => _.Name);
        return table.InputFields.GetValueOrDefault(name);
    }

    /// <summary>The named enum value of a type, or null. See <see cref="members"/>.</summary>
    public IntrospectionEnumValue? EnumValue(IntrospectionType? type, string name)
    {
        if (type is null)
        {
            return null;
        }

        var table = MembersOf(type);
        table.EnumValues ??= Build(type.EnumValues, _ => _.Name);
        return table.EnumValues.GetValueOrDefault(name);
    }

    /// <summary>The named directive, or null.</summary>
    public IntrospectionDirective? Directive(string name)
    {
        directivesByName ??= Build(Directives, _ => _.Name);
        return directivesByName.GetValueOrDefault(name);
    }

    Members MembersOf(IntrospectionType type)
    {
        if (members.TryGetValue(type.Name, out var table))
        {
            return table;
        }

        table = new();
        members[type.Name] = table;
        return table;
    }

    /// <summary>First wins, which is what the linear scan these replace would have found.</summary>
    static Dictionary<string, T> Build<T>(IReadOnlyList<T>? items, Func<T, string> name)
    {
        var table = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items ?? [])
        {
            table.TryAdd(name(item), item);
        }

        return table;
    }

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
