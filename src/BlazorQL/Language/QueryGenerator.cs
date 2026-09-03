namespace BlazorQL;

/// <summary>
/// Builds a document that selects every non-deprecated member of a type — what the documentation
/// explorer's generate-query buttons produce. A root operation type becomes the operation of its
/// kind; any other type is fetched through the root query fields that return it, and a type no
/// root field returns becomes a fragment. Required arguments become variables; nested composite
/// fields fall back to <see cref="LeafFiller"/>'s default field choice, cut off at the same depth.
/// </summary>
public static class QueryGenerator
{
    const int maxDepth = 3;

    /// <summary>Whether the type has members to select at all — a query or a fragment.</summary>
    public static bool CanGenerate(IntrospectionType type) =>
        type.Kind is "OBJECT" or "INTERFACE" or "UNION";

    /// <summary>
    /// Whether a runnable operation can be built: the type is a root type, a root field returns it,
    /// or a chain of fields reaches it. Anything else can only become a fragment, which is worth
    /// copying but has no business in the operation editor beside a run button — a document of one
    /// fragment answers "Document does not contain any operations".
    /// </summary>
    public static bool CanGenerateOperation(SchemaIndex schema, IntrospectionType type) =>
        CanGenerate(type) &&
        (type.Name == schema.QueryTypeName ||
         type.Name == schema.MutationTypeName ||
         type.Name == schema.SubscriptionTypeName ||
         RootFields(schema, type).Count > 0 ||
         schema.PathFromQuery(type.Name) is {Count: > 0});

    /// <summary>The non-deprecated root query fields returning the type.</summary>
    static IReadOnlyList<IntrospectionField> RootFields(SchemaIndex schema, IntrospectionType type) =>
        schema.RootFieldsReturning(type.Name);

    /// <summary>The generated document, or null when the type has nothing selectable.</summary>
    public static string? Generate(SchemaIndex schema, IntrospectionType type)
    {
        if (!CanGenerate(type))
        {
            return null;
        }

        var builder = new Builder(schema);
        if (type.Name == schema.QueryTypeName)
        {
            return builder.Operation("query", type);
        }

        if (type.Name == schema.MutationTypeName)
        {
            return builder.Operation("mutation", type);
        }

        if (type.Name == schema.SubscriptionTypeName)
        {
            return builder.Operation("subscription", type);
        }

        var rootFields = RootFields(schema, type);
        if (rootFields.Count > 0)
        {
            return builder.RootFieldsOperation(type, rootFields);
        }

        // Nothing at the root returns it, but something further in might. Nesting the selection
        // under that chain gives a document that runs; a fragment on its own does not, and the
        // button sits next to a run button.
        if (schema.PathFromQuery(type.Name) is {Count: > 0} path)
        {
            return builder.PathOperation(type, path);
        }

        return builder.Fragment(type);
    }

    sealed class Builder(SchemaIndex schema)
    {
        readonly List<(string Name, string Type)> variables = [];

        public string? Operation(string keyword, IntrospectionType type)
        {
            var lines = Selections(type, all: true, depth: 1);
            if (lines is null)
            {
                return null;
            }

            return Wrap($"{keyword} {type.Name}", lines);
        }

        public string? RootFieldsOperation(IntrospectionType type, IReadOnlyList<IntrospectionField> rootFields)
        {
            List<string> lines = [];
            foreach (var field in rootFields)
            {
                var fieldLines = Field(field, all: true, depth: 1);
                if (fieldLines is not null)
                {
                    lines.AddRange(fieldLines);
                }
            }

            if (lines.Count == 0)
            {
                return null;
            }

            return Wrap($"query {type.Name}", lines);
        }

        /// <summary>
        /// The type's selections, wrapped in the chain of fields that reaches it. Built inside out,
        /// so the arguments each step needs are collected before the operation declares them.
        /// </summary>
        public string? PathOperation(IntrospectionType type, IReadOnlyList<IntrospectionField> path)
        {
            var lines = Selections(type, all: true, depth: 1);
            if (lines is null)
            {
                return null;
            }

            for (var index = path.Count - 1; index >= 0; index--)
            {
                var field = path[index];
                lines = [$"{field.Name}{Arguments(field)} {{", .. lines.Select(_ => "  " + _), "}"];
            }

            return Wrap($"query {type.Name}", lines);
        }

        public string? Fragment(IntrospectionType type)
        {
            var lines = Selections(type, all: true, depth: 1);
            if (lines is null)
            {
                return null;
            }

            // A fragment cannot declare variables; any required argument stays a reference for the
            // operation that eventually spreads it.
            variables.Clear();
            return Wrap($"fragment {type.Name}Fields on {type.Name}", lines);
        }

        // The selection set for a type: every non-deprecated member when all is set, otherwise
        // LeafFiller's default choice. Unions select through an inline fragment per member type.
        // Null when nothing can be selected.
        List<string>? Selections(IntrospectionType type, bool all, int depth)
        {
            List<string> lines = [];
            if (type.Kind == "UNION")
            {
                foreach (var possibleType in type.PossibleTypes ?? [])
                {
                    var member = schema.Find(possibleType.Name);
                    if (member is null ||
                        Selections(member, all, depth) is not { } inner)
                    {
                        continue;
                    }

                    lines.Add($"... on {member.Name} {{");
                    lines.AddRange(inner.Select(_ => "  " + _));
                    lines.Add("}");
                }
            }
            else
            {
                foreach (var field in Members(type, all))
                {
                    var fieldLines = Field(field, all: false, depth);
                    if (fieldLines is not null)
                    {
                        lines.AddRange(fieldLines);
                    }
                }
            }

            if (lines.Count == 0)
            {
                return null;
            }

            return lines;
        }

        static IEnumerable<IntrospectionField> Members(IntrospectionType type, bool all)
        {
            var fields = type.Fields ?? [];
            if (all)
            {
                return fields.Where(_ => !_.IsDeprecated);
            }

            var names = LeafFiller.DefaultFieldNames(type);
            return fields.Where(_ => names.Contains(_.Name));
        }

        // One field with its required arguments and, for a composite type, its selection set.
        // Null when the field is composite but nothing under it can be selected — the variables
        // reserved for it are released again.
        List<string>? Field(IntrospectionField field, bool all, int depth)
        {
            var reserved = variables.Count;
            var arguments = Arguments(field);
            var fieldType = schema.Find(field.Type.Unwrap().Name);
            if (fieldType is null ||
                fieldType.Kind is "SCALAR" or "ENUM")
            {
                return [field.Name + arguments];
            }

            if (depth < maxDepth &&
                Selections(fieldType, all, depth + 1) is { } inner)
            {
                return [$"{field.Name}{arguments} {{", .. inner.Select(_ => "  " + _), "}"];
            }

            variables.RemoveRange(reserved, variables.Count - reserved);
            return null;
        }

        string Arguments(IntrospectionField field)
        {
            var required = field.Args
                .Where(_ => _ is { IsDeprecated: false, Type.Kind: "NON_NULL", DefaultValue: null })
                .ToList();
            if (required.Count == 0)
            {
                return "";
            }

            return $"({string.Join(", ", required.Select(_ => $"{_.Name}: ${Variable(field, _)}"))})";
        }

        // Variables take the argument's name; a clash falls back to fieldArgument, then a counter.
        string Variable(IntrospectionField field, IntrospectionInputValue argument)
        {
            var name = argument.Name;
            if (Taken(name))
            {
                name = field.Name + char.ToUpperInvariant(argument.Name[0]) + argument.Name[1..];
            }

            var candidate = name;
            var counter = 2;
            while (Taken(candidate))
            {
                candidate = name + counter++;
            }

            variables.Add((candidate, argument.Type.Display()));
            return candidate;
        }

        bool Taken(string name) =>
            variables.Any(_ => _.Name == name);

        string Wrap(string header, List<string> lines)
        {
            var builder = new StringBuilder(header);
            if (variables.Count > 0)
            {
                builder.Append('(');
                builder.AppendJoin(", ", variables.Select(_ => $"${_.Name}: {_.Type}"));
                builder.Append(')');
            }

            builder.Append(" {\n");
            foreach (var line in lines)
            {
                builder.Append("  ");
                builder.Append(line);
                builder.Append('\n');
            }

            builder.Append("}\n");
            return builder.ToString();
        }
    }
}
