using GraphQLParser.AST;

namespace BlazorQL;

/// <summary>
/// Fills selection sets in for fields of composite type that have none — GraphiQL's
/// fill-leafs-on-execute, ported over the GraphQLParser AST. Default field choice follows the
/// original: <c>id</c>, else <c>edges</c>, else <c>node</c>, else every leaf field.
/// </summary>
public static class LeafFiller
{
    public sealed record Insertion(int Index, string Text);

    public static (string Result, IReadOnlyList<Insertion> Insertions) Fill(SchemaIndex schema, string text)
    {
        var document = DocumentInfo.Parse(text);
        if (document.Document is null)
        {
            return (text, []);
        }

        var insertions = new List<Insertion>();
        foreach (var definition in document.Document.Definitions)
        {
            switch (definition)
            {
                case GraphQLOperationDefinition operation:
                    var rootName = operation.Operation switch
                    {
                        OperationType.Mutation => schema.MutationTypeName,
                        OperationType.Subscription => schema.SubscriptionTypeName,
                        _ => schema.QueryTypeName
                    };
                    Walk(schema, operation.SelectionSet, schema.Find(rootName), text, insertions);
                    break;

                case GraphQLFragmentDefinition fragment:
                    Walk(schema, fragment.SelectionSet, schema.Find(fragment.TypeCondition.Type.Name.StringValue), text, insertions);
                    break;
            }
        }

        if (insertions.Count == 0)
        {
            return (text, insertions);
        }

        var builder = new StringBuilder();
        var previous = 0;
        foreach (var insertion in insertions.OrderBy(_ => _.Index))
        {
            builder.Append(text, previous, insertion.Index - previous);
            builder.Append(insertion.Text);
            previous = insertion.Index;
        }

        builder.Append(text, previous, text.Length - previous);
        return (builder.ToString(), insertions);
    }

    static void Walk(SchemaIndex schema, GraphQLSelectionSet? selections, IntrospectionType? type, string text, List<Insertion> insertions)
    {
        if (selections is null ||
            type is null)
        {
            return;
        }

        foreach (var selection in selections.Selections)
        {
            switch (selection)
            {
                case GraphQLField field:
                    var definition = type.Fields?.FirstOrDefault(_ => _.Name == field.Name.StringValue);
                    if (definition is null)
                    {
                        continue;
                    }

                    var fieldType = schema.Find(definition.Type.Unwrap().Name);
                    if (field.SelectionSet is not null)
                    {
                        Walk(schema, field.SelectionSet, fieldType, text, insertions);
                        continue;
                    }

                    if (fieldType is null ||
                        IsLeaf(fieldType))
                    {
                        continue;
                    }

                    var built = BuildSelection(schema, fieldType, Indentation(text, field.Location.Start), depth: 1);
                    if (built is not null)
                    {
                        insertions.Add(new(field.Location.End, " " + built));
                    }

                    break;

                case GraphQLInlineFragment inline:
                    var condition = inline.TypeCondition?.Type.Name.StringValue;
                    Walk(schema, inline.SelectionSet, condition is null ? type : schema.Find(condition), text, insertions);
                    break;
            }
        }
    }

    static bool IsLeaf(IntrospectionType type) =>
        type.Kind is "SCALAR" or "ENUM";

    static string? BuildSelection(SchemaIndex schema, IntrospectionType type, string indent, int depth)
    {
        var names = DefaultFieldNames(type);
        if (names.Count == 0)
        {
            return null;
        }

        var inner = indent + new string(' ', depth * 2);
        var builder = new StringBuilder();
        builder.Append("{\n");
        foreach (var name in names)
        {
            builder.Append(inner);
            builder.Append("  ");
            builder.Append(name);

            var field = type.Fields?.FirstOrDefault(_ => _.Name == name);
            var fieldType = field is null ? null : schema.Find(field.Type.Unwrap().Name);
            if (fieldType is not null &&
                !IsLeaf(fieldType) &&
                // One level of recursion is enough for a best-effort fill; deeper cycles stop here.
                depth < 3 &&
                BuildSelection(schema, fieldType, indent, depth + 1) is { } nested)
            {
                builder.Append(' ');
                builder.Append(nested);
            }

            builder.Append('\n');
        }

        builder.Append(inner);
        builder.Append('}');
        return builder.ToString();
    }

    static IReadOnlyList<string> DefaultFieldNames(IntrospectionType type)
    {
        var fields = type.Fields ?? [];
        if (fields.Any(_ => _.Name == "id"))
        {
            return ["id"];
        }

        if (fields.Any(_ => _.Name == "edges"))
        {
            return ["edges"];
        }

        if (fields.Any(_ => _.Name == "node"))
        {
            return ["node"];
        }

        return [.. fields.Where(_ => _.Type.Unwrap().Kind is "SCALAR" or "ENUM").Select(_ => _.Name)];
    }

    static string Indentation(string text, int index)
    {
        var lineStart = index;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var indentEnd = lineStart;
        while (indentEnd < text.Length && text[indentEnd] is ' ' or '\t')
        {
            indentEnd++;
        }

        return text[lineStart..indentEnd];
    }
}
