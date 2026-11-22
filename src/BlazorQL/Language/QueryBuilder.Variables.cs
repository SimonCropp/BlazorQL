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
    /// <para>
    /// The document is the tab's, shared by every operation in it, so a variable comes out only when no
    /// operation left declares it. An edit that takes the whole operation out — unchecking its last
    /// field — leaves it declaring nothing, rather than comparing against the operation that moved into
    /// its place.
    /// </para>
    /// </remarks>
    public static Func<string, string?>? VariablesEdit(SchemaIndex schema, string before, string after, int definition)
    {
        var beforeDocument = ParseDocument(before);
        var afterDocument = ParseDocument(after);
        var previous = Declarations(beforeDocument, definition);
        var current = afterDocument?.Definitions.Count < beforeDocument?.Definitions.Count
            ? []
            : Declarations(afterDocument, definition);
        var declared = AllDeclarations(afterDocument);
        var added = current
            .Where(_ => !previous.ContainsKey(_.Key))
            .Select(_ => (_.Key, Value: VariableValue(schema, _.Value)))
            .ToList();
        var removed = previous.Keys
            .Where(_ => !declared.Contains(_))
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

    static Dictionary<string, GraphQLVariableDefinition> Declarations(GraphQLDocument? document, int definition)
    {
        var found = new Dictionary<string, GraphQLVariableDefinition>(StringComparer.Ordinal);
        if (document?.Definitions.ElementAtOrDefault(definition) is GraphQLOperationDefinition operation)
        {
            foreach (var declaration in operation.Variables?.Items ?? [])
            {
                found.TryAdd(declaration.Variable.Name.StringValue, declaration);
            }
        }

        return found;
    }

    /// <summary>Every variable any operation in the document declares.</summary>
    static HashSet<string> AllDeclarations(GraphQLDocument? document) =>
        (document?.Definitions ?? [])
        .OfType<GraphQLOperationDefinition>()
        .SelectMany(_ => _.Variables?.Items ?? [])
        .Select(_ => _.Variable.Name.StringValue)
        .ToHashSet(StringComparer.Ordinal);

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
