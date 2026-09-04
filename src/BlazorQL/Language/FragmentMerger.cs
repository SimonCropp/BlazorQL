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

        var fragments = new Dictionary<string, GraphQLFragmentDefinition>(StringComparer.Ordinal);
        foreach (var fragment in document.Fragments)
        {
            // First wins, matching the validator's walk. A duplicate name is its own error there;
            // building the lookup must not be what reports it, and must not throw.
            fragments.TryAdd(fragment.FragmentName.Name.StringValue, fragment);
        }

        // Inlining a fragment that spreads itself, directly or through others, cannot terminate.
        // NoFragmentCycles is a deliberate validator gap, so nothing upstream keeps such a document
        // from reaching here, and on WebAssembly the resulting stack overflow takes the page down.
        if (FindCycle(fragments) is {} cycle)
        {
            return (false, null, cycle);
        }

        var definitions = new List<ASTNode>();
        foreach (var definition in document.Document.Definitions)
        {
            if (definition is GraphQLFragmentDefinition)
            {
                continue;
            }

            if (definition is GraphQLOperationDefinition operation)
            {
                operation.SelectionSet = Flatten(operation.SelectionSet, fragments);
            }

            definitions.Add(definition);
        }

        // A spread carrying a directive is left where it is -- inlining it would change what the
        // document means -- so the definition it names has to survive the merge, and so does
        // everything that one reaches. Dropping them turned a valid document into an unknown
        // fragment with one click.
        var kept = new HashSet<string>(StringComparer.Ordinal);
        Reachable(definitions.OfType<GraphQLOperationDefinition>().Select(_ => _.SelectionSet), fragments, kept);

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fragment in document.Document.Definitions.OfType<GraphQLFragmentDefinition>())
        {
            var name = fragment.FragmentName.Name.StringValue;
            if (kept.Contains(name) &&
                emitted.Add(name))
            {
                definitions.Add(fragment);
            }
        }

        document.Document.Definitions.Clear();
        foreach (var definition in definitions)
        {
            document.Document.Definitions.Add(definition);
        }

        return (true, Formatter.FormatGraphQL(Print(document.Document)), null);
    }

    /// <summary>
    /// The names of every fragment still spread from <paramref name="roots"/> once the inlining is
    /// done, following the definitions of those to whatever they spread in turn.
    /// </summary>
    static void Reachable(
        IEnumerable<GraphQLSelectionSet?> roots,
        Dictionary<string, GraphQLFragmentDefinition> fragments,
        HashSet<string> names)
    {
        var pending = new Queue<GraphQLSelectionSet?>(roots);
        while (pending.Count > 0)
        {
            foreach (var spread in Spreads(pending.Dequeue()))
            {
                var name = spread.FragmentName.Name.StringValue;
                if (names.Add(name) &&
                    fragments.TryGetValue(name, out var fragment))
                {
                    pending.Enqueue(fragment.SelectionSet);
                }
            }
        }
    }

    /// <summary>
    /// The graphql-js NoFragmentCycles message for the first cycle in the fragment graph, or null
    /// when it is acyclic. Spreads carrying directives count: they are not inlined, but a document
    /// containing any cycle is invalid, and refusing is more use than a partial merge.
    /// </summary>
    static string? FindCycle(Dictionary<string, GraphQLFragmentDefinition> fragments)
    {
        var acyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in fragments.Keys)
        {
            if (FindCycle(name, fragments, acyclic, [], new(StringComparer.Ordinal)) is {} cycle)
            {
                return cycle;
            }
        }

        return null;
    }

    static string? FindCycle(
        string name,
        Dictionary<string, GraphQLFragmentDefinition> fragments,
        HashSet<string> acyclic,
        List<string> path,
        HashSet<string> onPath)
    {
        if (acyclic.Contains(name) ||
            !fragments.TryGetValue(name, out var fragment))
        {
            return null;
        }

        path.Add(name);
        onPath.Add(name);
        foreach (var spread in Spreads(fragment.SelectionSet))
        {
            var target = spread.FragmentName.Name.StringValue;
            if (onPath.Contains(target))
            {
                var via = path
                    .Skip(path.IndexOf(target) + 1)
                    .Select(_ => $"\"{_}\"")
                    .ToArray();
                var through = via.Length != 0 ? $" via {string.Join(", ", via)}" : "";
                return $"Cannot spread fragment \"{target}\" within itself{through}.";
            }

            if (FindCycle(target, fragments, acyclic, path, onPath) is {} cycle)
            {
                return cycle;
            }
        }

        onPath.Remove(name);
        path.RemoveAt(path.Count - 1);
        acyclic.Add(name);
        return null;
    }

    /// <summary>Every fragment spread in a selection set, however deeply nested.</summary>
    static IEnumerable<GraphQLFragmentSpread> Spreads(GraphQLSelectionSet? selections)
    {
        foreach (var selection in selections?.Selections ?? [])
        {
            switch (selection)
            {
                case GraphQLFragmentSpread spread:
                    yield return spread;
                    break;

                case GraphQLField {SelectionSet: {} nested}:
                    foreach (var inner in Spreads(nested))
                    {
                        yield return inner;
                    }

                    break;

                case GraphQLInlineFragment inline:
                    foreach (var inner in Spreads(inline.SelectionSet))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    static string Print(GraphQLDocument document)
    {
        var writer = new StringWriter();
        new SDLPrinter().PrintAsync(document, writer).AsTask().GetAwaiter().GetResult();
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
            if (selection is GraphQLFragmentSpread {Directives: not {Items.Count: > 0}} spread)
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
                case GraphQLField {Directives: not {Items.Count: > 0}} field:
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

                case GraphQLInlineFragment {Directives: not {Items.Count: > 0}} inline:
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
