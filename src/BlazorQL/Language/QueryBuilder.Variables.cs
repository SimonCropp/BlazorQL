namespace BlazorQL;

public static partial class QueryBuilder
{
    static readonly JsonDocumentOptions variablesReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static readonly JsonSerializerOptions variablesWriteOptions = new()
    {
        WriteIndented = true,
        // The editor's model is read with LF line endings, whatever the platform.
        NewLine = "\n"
    };

    /// <summary>
    /// The edit to the variables document that keeps it in step with an operation edit: a variable the
    /// operation edit declared goes in with its default, or a placeholder of its type where it has none,
    /// and one it took out comes out. Null when the edit declared and dropped nothing.
    /// </summary>
    /// <remarks>
    /// A variable the document already carries keeps the value it has, since that is one somebody
    /// typed. The edit is left alone too where the document is not a JSON object, rather than throwing
    /// away whatever half-written text is there.
    /// </remarks>
    public static Func<string, string?>? VariablesEdit(SchemaIndex schema, string before, string after, int definition)
    {
        var previous = Declarations(before, definition);
        var current = Declarations(after, definition);
        var added = current
            .Where(_ => !previous.ContainsKey(_.Key))
            .Select(_ => (_.Key, Value: VariableValue(schema, _.Value)))
            .ToList();
        var removed = previous.Keys
            .Where(_ => !current.ContainsKey(_))
            .ToList();
        if (added.Count == 0 &&
            removed.Count == 0)
        {
            return null;
        }

        return variables =>
        {
            if (ReadVariables(variables) is not { } root)
            {
                return null;
            }

            foreach (var name in removed)
            {
                root.Remove(name);
            }

            foreach (var (name, value) in added)
            {
                root.TryAdd(name, value);
            }

            return root.ToJsonString(variablesWriteOptions);
        };
    }

    static Dictionary<string, GraphQLVariableDefinition> Declarations(string text, int definition)
    {
        var found = new Dictionary<string, GraphQLVariableDefinition>(StringComparer.Ordinal);
        if (Parse(text, definition) is GraphQLOperationDefinition operation)
        {
            foreach (var declaration in operation.Variables?.Items ?? [])
            {
                found.TryAdd(declaration.Variable.Name.StringValue, declaration);
            }
        }

        return found;
    }

    static JsonObject? ReadVariables(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: variablesReadOptions) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static JsonNode? VariableValue(SchemaIndex schema, GraphQLVariableDefinition declaration)
    {
        var type = TypeOf(declaration.Type);
        var value = Json(declaration.DefaultValue ?? DefaultValue(schema, type));
        // The placeholder is a single item, which coercion takes for a list; the document says what
        // the variable is, so a list variable gets the list.
        if (type.Kind == "NON_NULL" ? type.OfType?.Kind == "LIST" : type.Kind == "LIST")
        {
            if (value is not JsonArray)
            {
                return new JsonArray(value);
            }
        }

        return value;
    }

    static TypeRef TypeOf(GraphQLType type) =>
        type switch
        {
            GraphQLNonNullType nonNull => new()
            {
                Kind = "NON_NULL",
                OfType = TypeOf(nonNull.Type)
            },
            GraphQLListType list => new()
            {
                Kind = "LIST",
                OfType = TypeOf(list.Type)
            },
            GraphQLNamedType named => new()
            {
                Kind = "NAMED",
                Name = named.Name.StringValue
            },
            _ => new()
        };

    /// <summary>A constant GraphQL value as the variables document writes it.</summary>
    public static JsonNode? Json(GraphQLValue value) =>
        value switch
        {
            GraphQLIntValue or GraphQLFloatValue => JsonNode.Parse(Print(value)),
            GraphQLStringValue text => JsonValue.Create(text.Value.ToString()),
            GraphQLTrueBooleanValue => JsonValue.Create(true),
            GraphQLFalseBooleanValue => JsonValue.Create(false),
            GraphQLEnumValue enumValue => JsonValue.Create(enumValue.Name.StringValue),
            GraphQLListValue list => new JsonArray((list.Values ?? []).Select(Json).ToArray()),
            GraphQLObjectValue objectValue => new JsonObject(
                (objectValue.Fields ?? []).Select(_ => KeyValuePair.Create(_.Name.StringValue, Json(_.Value)))),
            _ => null
        };
}
