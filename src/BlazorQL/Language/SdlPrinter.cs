namespace BlazorQL;

/// <summary>
/// Prints an introspected schema as SDL — the text the SDL view shows and the validator's schema
/// is built from. Descriptions ride along as block strings; introspection-only (<c>__*</c>) types
/// are omitted, as are the built-in scalars and directives every schema carries implicitly.
/// </summary>
public static class SdlPrinter
{
    static string[] builtInScalars = ["Int", "Float", "String", "Boolean", "ID"];
    static string[] builtInDirectives = ["skip", "include", "deprecated", "specifiedBy", "oneOf"];

    public static string Print(SchemaIndex schema)
    {
        var builder = new StringBuilder();

        if (schema.Description is not null ||
            HasNonDefaultRoots(schema))
        {
            Description(builder, schema.Description, "");
            builder.AppendLine("schema {");
            builder.AppendLine($"  query: {schema.QueryTypeName}");
            if (schema.MutationTypeName is not null)
            {
                builder.AppendLine($"  mutation: {schema.MutationTypeName}");
            }

            if (schema.SubscriptionTypeName is not null)
            {
                builder.AppendLine($"  subscription: {schema.SubscriptionTypeName}");
            }

            builder.AppendLine("}");
        }

        foreach (var directive in schema.Directives)
        {
            if (builtInDirectives.Contains(directive.Name))
            {
                continue;
            }

            builder.AppendLine();
            Description(builder, directive.Description, "");
            builder.Append($"directive @{directive.Name}");
            Arguments(builder, directive.Args, "");
            if (directive.IsRepeatable)
            {
                builder.Append(" repeatable");
            }

            builder.AppendLine($" on {string.Join(" | ", directive.Locations)}");
        }

        foreach (var type in schema.Types)
        {
            if (type.Name.StartsWith("__", StringComparison.Ordinal) ||
                (type.Kind == "SCALAR" && builtInScalars.Contains(type.Name)))
            {
                continue;
            }

            builder.AppendLine();
            PrintType(builder, type);
        }

        return builder.ToString();
    }

    static bool HasNonDefaultRoots(SchemaIndex schema) =>
        schema.QueryTypeName != "Query" ||
        (schema.MutationTypeName is not null && schema.MutationTypeName != "Mutation") ||
        (schema.SubscriptionTypeName is not null && schema.SubscriptionTypeName != "Subscription");

    static void PrintType(StringBuilder builder, IntrospectionType type)
    {
        Description(builder, type.Description, "");
        switch (type.Kind)
        {
            case "SCALAR":
                builder.Append($"scalar {type.Name}");
                if (type.SpecifiedByURL is not null)
                {
                    builder.Append($" @specifiedBy(url: {Quote(type.SpecifiedByURL)})");
                }

                builder.AppendLine();
                break;

            case "OBJECT":
            case "INTERFACE":
                builder.Append(type.Kind == "OBJECT" ? "type " : "interface ");
                builder.Append(type.Name);
                if (type.Interfaces is {Count: > 0} interfaces)
                {
                    builder.Append(" implements ");
                    builder.AppendJoin(" & ", interfaces.Select(_ => _.Unwrap().Name));
                }

                builder.AppendLine(" {");
                foreach (var field in type.Fields ?? [])
                {
                    Description(builder, field.Description, "  ");
                    builder.Append($"  {field.Name}");
                    Arguments(builder, field.Args, "  ");
                    builder.Append($": {field.Type.Display()}");
                    Deprecation(builder, field.IsDeprecated, field.DeprecationReason);
                    builder.AppendLine();
                }

                builder.AppendLine("}");
                break;

            case "UNION":
                builder.AppendLine($"union {type.Name} = {string.Join(" | ", (type.PossibleTypes ?? []).Select(_ => _.Unwrap().Name))}");
                break;

            case "ENUM":
                builder.AppendLine($"enum {type.Name} {{");
                foreach (var value in type.EnumValues ?? [])
                {
                    Description(builder, value.Description, "  ");
                    builder.Append($"  {value.Name}");
                    Deprecation(builder, value.IsDeprecated, value.DeprecationReason);
                    builder.AppendLine();
                }

                builder.AppendLine("}");
                break;

            case "INPUT_OBJECT":
                builder.AppendLine($"input {type.Name} {{");
                foreach (var field in type.InputFields ?? [])
                {
                    Description(builder, field.Description, "  ");
                    builder.Append($"  {field.Name}: {field.Type.Display()}");
                    if (field.DefaultValue is not null)
                    {
                        builder.Append($" = {field.DefaultValue}");
                    }

                    Deprecation(builder, field.IsDeprecated, field.DeprecationReason);
                    builder.AppendLine();
                }

                builder.AppendLine("}");
                break;
        }
    }

    static void Arguments(StringBuilder builder, IReadOnlyList<IntrospectionInputValue>? args, string indent)
    {
        if (args is not {Count: > 0})
        {
            return;
        }

        // Any described argument forces the multi-line form, where descriptions are legal.
        if (args.Any(_ => _.Description is not null))
        {
            builder.AppendLine("(");
            foreach (var argument in args)
            {
                Description(builder, argument.Description, indent + "  ");
                builder.Append($"{indent}  {Argument(argument)}");
                builder.AppendLine();
            }

            builder.Append($"{indent})");
            return;
        }

        builder.Append('(');
        builder.AppendJoin(", ", args.Select(Argument));
        builder.Append(')');
    }

    static string Argument(IntrospectionInputValue argument)
    {
        var text = $"{argument.Name}: {argument.Type.Display()}";
        if (argument.DefaultValue is not null)
        {
            text += $" = {argument.DefaultValue}";
        }

        if (argument.IsDeprecated)
        {
            text += DeprecationText(argument.DeprecationReason);
        }

        return text;
    }

    static void Deprecation(StringBuilder builder, bool deprecated, string? reason)
    {
        if (deprecated)
        {
            builder.Append(DeprecationText(reason));
        }
    }

    static string DeprecationText(string? reason)
    {
        if (reason is not null)
        {
            return $" @deprecated(reason: {Quote(reason)})";
        }

        return " @deprecated";
    }

    static void Description(StringBuilder builder, string? description, string indent)
    {
        if (description is null)
        {
            return;
        }

        builder.AppendLine($"{indent}\"\"\"");
        foreach (var line in description.Replace("\r\n", "\n").Split('\n'))
        {
            builder.AppendLine($"{indent}{line.Replace("\"\"\"", "\\\"\"\"")}");
        }

        builder.AppendLine($"{indent}\"\"\"");
    }

    /// <summary>Escapes a string as a JSON string literal, which is also GraphQL's escaping.</summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "string has a built-in converter, so nothing here is discovered by reflection.")]
    static string Quote(string text) =>
        JsonSerializer.Serialize(text);
}
