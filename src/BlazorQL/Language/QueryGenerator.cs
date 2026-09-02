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

    public static bool CanGenerate(IntrospectionType type) =>
        type.Kind is "OBJECT" or "INTERFACE" or "UNION";

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

        var rootFields = schema.Find(schema.QueryTypeName)?.Fields?
            .Where(_ => !_.IsDeprecated && _.Type.Unwrap().Name == type.Name)
            .ToList() ?? [];
        return rootFields.Count > 0
            ? builder.RootFieldsOperation(type, rootFields)
            : builder.Fragment(type);
    }

    sealed class Builder(SchemaIndex schema)
    {
        readonly List<(string Name, string Type)> variables = [];

        public string? Operation(string keyword, IntrospectionType type)
        {
            var lines = Selections(type, all: true, depth: 1);
            return lines is null
                ? null
                : Wrap($"{keyword} {type.Name}", lines);
        }

        public string? RootFieldsOperation(IntrospectionType type, List<IntrospectionField> rootFields)
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

            return lines.Count == 0
                ? null
                : Wrap($"query {type.Name}", lines);
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

            return lines.Count == 0
                ? null
                : lines;
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
                .Where(_ => !_.IsDeprecated && _.Type.Kind == "NON_NULL" && _.DefaultValue is null)
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
