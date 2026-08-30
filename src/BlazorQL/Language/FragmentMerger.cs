using GraphQLParser.AST;

namespace BlazorQL;

/// <summary>
/// Inlines every named fragment definition into the operations that spread it, deduplicating the
/// merged fields — GraphiQL's merge action, ported over the GraphQLParser AST. Spreads and fields
/// carrying directives are left alone (inlining them would change semantics).
/// </summary>
public static class FragmentMerger
{
    public static (bool Ok, string? Text, string? Error) Merge(string text)
    {
        var document = DocumentInfo.Parse(text);
        if (document.Document is null)
        {
            return (false, null, $"Syntax Error: {document.SyntaxError}");
        }

        var fragments = document.Fragments.ToDictionary(_ => _.FragmentName.Name.StringValue, _ => _);
        var definitions = new List<ASTNode>();
        foreach (var definition in document.Document.Definitions)
        {
            if (definition is GraphQLFragmentDefinition)
            {
                continue;
            }

            if (definition is GraphQLOperationDefinition operation &&
                operation.SelectionSet is not null)
            {
                operation.SelectionSet = Flatten(operation.SelectionSet, fragments);
            }

            definitions.Add(definition);
        }

        document.Document.Definitions.Clear();
        foreach (var definition in definitions)
        {
            document.Document.Definitions.Add(definition);
        }

        return (true, Formatter.FormatGraphQL(Print(document.Document)), null);
    }

    static string Print(GraphQLDocument document)
    {
        var writer = new StringWriter();
        new GraphQLParser.Visitors.SDLPrinter().PrintAsync(document, writer).AsTask().GetAwaiter().GetResult();
        return writer.ToString();
    }

    static GraphQLSelectionSet Flatten(GraphQLSelectionSet selections, Dictionary<string, GraphQLFragmentDefinition> fragments)
    {
        var seenSpreads = new HashSet<string>();
        var output = new List<ASTNode>();
        foreach (var selection in Inline(selections.Selections, fragments, seenSpreads))
        {
            output.Add(selection);
        }

        return new(Deduplicate(output, fragments));
    }

    static IEnumerable<ASTNode> Inline(
        List<ASTNode> selections,
        Dictionary<string, GraphQLFragmentDefinition> fragments,
        HashSet<string> seenSpreads)
    {
        foreach (var selection in selections)
        {
            if (selection is GraphQLFragmentSpread spread &&
                spread.Directives is not {Items.Count: > 0})
            {
                var name = spread.FragmentName.Name.StringValue;
                if (!seenSpreads.Add(name))
                {
                    continue;
                }

                if (fragments.TryGetValue(name, out var fragment))
                {
                    // Inline as an inline fragment so the type condition is preserved; the
                    // deduplication pass folds it away when the condition matches the parent.
                    yield return new GraphQLInlineFragment(fragment.SelectionSet)
                    {
                        TypeCondition = new(fragment.TypeCondition.Type)
                    };
                    continue;
                }
            }

            yield return selection;
        }
    }

    static List<ASTNode> Deduplicate(List<ASTNode> selections, Dictionary<string, GraphQLFragmentDefinition> fragments)
    {
        var byName = new Dictionary<string, GraphQLField>();
        var output = new List<ASTNode>();
        foreach (var selection in selections)
        {
            switch (selection)
            {
                case GraphQLField field when field.Directives is not {Items.Count: > 0}:
                    var key = field.Alias?.Name.StringValue ?? field.Name.StringValue;
                    if (byName.TryGetValue(key, out var existing))
                    {
                        if (existing.SelectionSet is not null &&
                            field.SelectionSet is not null)
                        {
                            existing.SelectionSet.Selections.AddRange(field.SelectionSet.Selections);
                            existing.SelectionSet = Flatten(existing.SelectionSet, fragments);
                        }

                        continue;
                    }

                    if (field.SelectionSet is not null)
                    {
                        field.SelectionSet = Flatten(field.SelectionSet, fragments);
                    }

                    byName[key] = field;
                    output.Add(field);
                    break;

                case GraphQLInlineFragment inline when inline.Directives is not {Items.Count: > 0} && inline.SelectionSet is not null:
                    inline.SelectionSet = Flatten(inline.SelectionSet, fragments);
                    output.Add(inline);
                    break;

                default:
                    output.Add(selection);
                    break;
            }
        }

        return output;
    }
}
