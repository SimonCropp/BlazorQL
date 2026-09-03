namespace BlazorQL;

/// <summary>
/// Checks the variables document against the active operation's variable declarations: unknown
/// variables, missing non-null ones without defaults, and recursive type mismatches over scalars,
/// enums, lists, and input objects — the C# stand-in for GraphiQL's generated JSON Schema.
/// </summary>
public static class VariablesChecker
{
    public static IReadOnlyList<string> Check(SchemaIndex schema, GraphQLOperationDefinition operation, JsonElement? variables)
    {
        var errors = new List<string>();
        var declared = new Dictionary<string, GraphQLVariableDefinition>(StringComparer.Ordinal);
        foreach (var variable in operation.Variables?.Items ?? [])
        {
            // First wins. UniqueVariableNames is a known validator gap, so a document declaring one
            // name twice does reach here, and building this lookup is not where that gets reported
            // -- nor where the whole diagnostics pass may be lost to an exception.
            declared.TryAdd(variable.Variable.Name.StringValue, variable);
        }

        if (variables is {ValueKind: JsonValueKind.Object} value)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!declared.TryGetValue(property.Name, out var definition))
                {
                    errors.Add($"Variable ${property.Name} is not declared by the operation.");
                    continue;
                }

                CheckValue(schema, $"${property.Name}", property.Value, definition.Type, errors);
            }
        }

        foreach (var (name, definition) in declared)
        {
            var provided = variables is
                           {
                               ValueKind: JsonValueKind.Object
                           } given &&
                           given.TryGetProperty(name, out _);
            if (!provided &&
                definition.Type is GraphQLNonNullType &&
                definition.DefaultValue is null)
            {
                errors.Add($"Variable ${name} is non-null and has no default — a value is required.");
            }
        }

        return errors;
    }

    // ReSharper disable TailRecursiveCall
    static void CheckValue(SchemaIndex schema, string path, JsonElement value, GraphQLType type, List<string> errors)
    {
        switch (type)
        {
            case GraphQLNonNullType nonNull:
                if (value.ValueKind == JsonValueKind.Null)
                {
                    errors.Add($"{path} must not be null.");
                    return;
                }

                CheckValue(schema, path, value, nonNull.Type, errors);
                return;

            case GraphQLListType list:
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return;
                }

                if (value.ValueKind != JsonValueKind.Array)
                {
                    // A single value coerces to a one-element list per spec.
                    CheckValue(schema, path, value, list.Type, errors);
                    return;
                }

                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    CheckValue(schema, $"{path}[{index++}]", item, list.Type, errors);
                }

                return;

            case GraphQLNamedType named:
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return;
                }

                CheckNamed(schema, path, value, named.Name.StringValue, errors);
                return;
        }
    }
    // ReSharper restore TailRecursiveCall

    static void CheckNamed(SchemaIndex schema, string path, JsonElement value, string typeName, List<string> errors)
    {
        switch (typeName)
        {
            case "Int":
                if (value.ValueKind != JsonValueKind.Number || value.GetRawText().Contains('.'))
                {
                    errors.Add($"{path} expects an Int.");
                }

                return;

            case "Float":
                if (value.ValueKind != JsonValueKind.Number)
                {
                    errors.Add($"{path} expects a Float.");
                }

                return;

            case "String" or "ID":
                if (value.ValueKind != JsonValueKind.String &&
                    (typeName != "ID" || value.ValueKind != JsonValueKind.Number))
                {
                    errors.Add($"{path} expects a {typeName}.");
                }

                return;

            case "Boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add($"{path} expects a Boolean.");
                }

                return;
        }

        var type = schema.Find(typeName);
        switch (type?.Kind)
        {
            case "ENUM":
                if (value.ValueKind != JsonValueKind.String ||
                    type.EnumValues?.Any(_ => _.Name == value.GetString()) is not true)
                {
                    errors.Add($"{path} expects a {typeName} value: {string.Join(", ", (type.EnumValues ?? []).Select(_ => _.Name))}.");
                }

                return;

            case "INPUT_OBJECT":
                if (value.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{path} expects a {typeName} object.");
                    return;
                }

                var fields = type.InputFields ?? [];
                foreach (var property in value.EnumerateObject())
                {
                    var field = schema.InputField(type, property.Name);
                    if (field is null)
                    {
                        errors.Add($"{path}.{property.Name} is not a field of {typeName}.");
                        continue;
                    }

                    CheckInputValue(schema, $"{path}.{property.Name}", property.Value, field.Type, errors);
                }

                foreach (var field in fields)
                {
                    if (field.Type.Kind == "NON_NULL" &&
                        field.DefaultValue is null &&
                        !value.TryGetProperty(field.Name, out _))
                    {
                        errors.Add($"{path}.{field.Name} is required by {typeName}.");
                    }
                }

                return;
        }

        // Custom scalars accept anything.
    }

    // ReSharper disable TailRecursiveCall
    static void CheckInputValue(SchemaIndex schema, string path, JsonElement value, TypeRef type, List<string> errors)
    {
        switch (type.Kind)
        {
            case "NON_NULL":
                if (value.ValueKind == JsonValueKind.Null)
                {
                    errors.Add($"{path} must not be null.");
                    return;
                }

                CheckInputValue(schema, path, value, type.OfType!, errors);
                return;

            case "LIST":
                if (value.ValueKind == JsonValueKind.Null)
                {
                    return;
                }

                if (value.ValueKind != JsonValueKind.Array)
                {
                    CheckInputValue(schema, path, value, type.OfType!, errors);
                    return;
                }

                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    CheckInputValue(schema, $"{path}[{index++}]", item, type.OfType!, errors);
                }

                return;

            default:
                if (value.ValueKind != JsonValueKind.Null &&
                    type.Name is { } name)
                {
                    CheckNamed(schema, path, value, name, errors);
                }

                return;
        }
    }
    // ReSharper restore TailRecursiveCall
}
