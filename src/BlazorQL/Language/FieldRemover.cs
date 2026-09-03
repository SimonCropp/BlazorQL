namespace BlazorQL;

/// <summary>
/// Deletes the field a GraphQL error points at, located by that error's response path.
/// </summary>
/// <remarks>
/// The path names response keys, so an aliased field matches on its alias, and list indices are no
/// part of it — a list's selection set is written once however many elements come back. Two knock-on
/// edits keep the result a document the server will still accept: removing the last selection in a
/// set would leave an empty pair of braces, so the removal takes the parent field with it, and a
/// variable left declared but unused is a validation error in its own right, so those go too.
/// </remarks>
public static class FieldRemover
{
    /// <summary>
    /// <paramref name="text"/> with the field at <paramref name="path"/> removed, or null when
    /// there is no such removal to make: the path does not resolve (a response left over from an
    /// edited query), or the field is the only thing the operation selects, and taking it out would
    /// leave an empty operation rather than a smaller one.
    /// </summary>
    public static string? Remove(string text, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
        {
            return null;
        }

        var info = DocumentInfo.Parse(text);
        if (info.Document is null)
        {
            return null;
        }

        var fragments = info.Fragments
            .ToDictionary(_ => _.FragmentName.Name.StringValue, _ => _, StringComparer.Ordinal);

        foreach (var operation in info.Document.Definitions.OfType<GraphQLOperationDefinition>())
        {
            List<Step> chain = [];
            if (!Resolve(operation.SelectionSet, path, 0, fragments, chain))
            {
                continue;
            }

            var target = Target(chain);
            if (target is null)
            {
                return null;
            }

            return RemoveUnusedVariables(Cut(text, target.Location.Start, target.Location.End));
        }

        return null;
    }

    /// <summary>One field on the way down, with the set it was selected in.</summary>
    readonly record struct Step(GraphQLSelectionSet Set, GraphQLField Field);

    /// <summary>
    /// Walks the path through the document, recording the fields it passes. Fragments are followed:
    /// a path segment can perfectly well be satisfied by a field the query only mentions through a
    /// spread.
    /// </summary>
    static bool Resolve(
        GraphQLSelectionSet? set,
        IReadOnlyList<string> path,
        int depth,
        IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments,
        List<Step> chain)
    {
        if (set is null ||
            depth == path.Count)
        {
            return false;
        }

        foreach (var selection in set.Selections)
        {
            switch (selection)
            {
                case GraphQLField field:
                    var key = field.Alias?.Name.StringValue ?? field.Name.StringValue;
                    if (key != path[depth])
                    {
                        continue;
                    }

                    chain.Add(new(set, field));
                    if (depth == path.Count - 1)
                    {
                        return true;
                    }

                    if (Resolve(field.SelectionSet, path, depth + 1, fragments, chain))
                    {
                        return true;
                    }

                    // The name matched but nothing under it did, so this was the wrong branch.
                    chain.RemoveAt(chain.Count - 1);
                    continue;

                case GraphQLInlineFragment inline:
                    if (Resolve(inline.SelectionSet, path, depth, fragments, chain))
                    {
                        return true;
                    }

                    continue;

                case GraphQLFragmentSpread spread
                    when fragments.TryGetValue(spread.FragmentName.Name.StringValue, out var fragment):
                    if (Resolve(fragment.SelectionSet, path, depth, fragments, chain))
                    {
                        return true;
                    }

                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// The field to actually cut. Removing the only selection in a set leaves braces around
    /// nothing, so that case climbs to the parent instead; reaching the operation's own set means
    /// there is no removal that leaves a valid document.
    /// </summary>
    static GraphQLField? Target(List<Step> chain)
    {
        var index = chain.Count - 1;
        while (chain[index].Set.Selections.Count == 1)
        {
            if (index == 0)
            {
                return null;
            }

            index--;
        }

        return chain[index].Field;
    }

    /// <summary>
    /// Cuts a span, taking the whole line with it when the span had the line to itself. Leaving the
    /// indentation and the newline behind would grow a blank line every time the button is used.
    /// </summary>
    static string Cut(string text, int start, int end)
    {
        var from = start;
        while (from > 0 &&
               text[from - 1] is ' ' or '\t')
        {
            from--;
        }

        var alone = from == 0 || text[from - 1] == '\n';
        if (!alone)
        {
            from = start;
        }

        var to = end;
        while (to < text.Length &&
               text[to] is ' ' or '\t')
        {
            to++;
        }

        if (alone)
        {
            if (to < text.Length &&
                text[to] == '\r')
            {
                to++;
            }

            if (to < text.Length &&
                text[to] == '\n')
            {
                to++;
            }
        }
        else
        {
            // Mid-line, so one separating space is all that should survive.
            to = end < to ? end + 1 : end;
        }

        return text[..from] + text[to..];
    }

    /// <summary>
    /// Drops variable definitions nothing references any more. An unused variable fails the spec's
    /// NoUnusedVariables rule, so leaving one behind would trade a failing field for a document the
    /// server rejects outright.
    /// </summary>
    static string RemoveUnusedVariables(string text)
    {
        var document = DocumentInfo.Parse(text).Document;
        if (document is null)
        {
            return text;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in document.Definitions)
        {
            switch (definition)
            {
                case GraphQLOperationDefinition operation:
                    CollectUsed(operation.SelectionSet, used);
                    break;

                case GraphQLFragmentDefinition fragment:
                    CollectUsed(fragment.SelectionSet, used);
                    break;
            }
        }

        // Right to left throughout, so an earlier cut's offsets are still good after a later one.
        var operations = document.Definitions
            .OfType<GraphQLOperationDefinition>()
            .Where(_ => _.Variables is {Items.Count: > 0})
            .OrderByDescending(_ => _.Location.Start);

        foreach (var operation in operations)
        {
            var variables = operation.Variables!;
            var unused = variables.Items
                .Where(_ => !used.Contains(_.Variable.Name.StringValue))
                .ToList();

            if (unused.Count == 0)
            {
                continue;
            }

            // Emptying the list would leave "query Name()", which does not parse, so the whole
            // parenthesised block goes instead of its last member.
            if (unused.Count == variables.Items.Count)
            {
                text = text[..variables.Location.Start] + text[variables.Location.End..];
                continue;
            }

            foreach (var variable in unused.OrderByDescending(_ => _.Location.Start))
            {
                text = CutVariable(text, variable.Location.Start, variable.Location.End);
            }
        }

        return text;
    }

    /// <summary>
    /// A variable definition sits in a comma or space separated list on one line, so the line rules
    /// in <see cref="Cut"/> do not apply — the separator on one side has to go with it.
    /// </summary>
    static string CutVariable(string text, int start, int end)
    {
        var to = end;
        while (to < text.Length &&
               text[to] is ' ' or '\t' or ',')
        {
            to++;
        }

        // Nothing followed, so the separator before it is the one to take.
        if (to < text.Length &&
            text[to] == ')')
        {
            var from = start;
            while (from > 0 &&
                   text[from - 1] is ' ' or '\t' or ',')
            {
                from--;
            }

            return text[..from] + text[end..];
        }

        return text[..start] + text[to..];
    }

    static void CollectUsed(GraphQLSelectionSet? set, HashSet<string> used)
    {
        if (set is null)
        {
            return;
        }

        foreach (var selection in set.Selections)
        {
            switch (selection)
            {
                case GraphQLField field:
                    CollectUsed(field.Arguments, used);
                    CollectUsed(field.Directives, used);
                    CollectUsed(field.SelectionSet, used);
                    break;

                case GraphQLInlineFragment inline:
                    CollectUsed(inline.Directives, used);
                    CollectUsed(inline.SelectionSet, used);
                    break;

                case GraphQLFragmentSpread spread:
                    CollectUsed(spread.Directives, used);
                    break;
            }
        }
    }

    static void CollectUsed(GraphQLArguments? arguments, HashSet<string> used)
    {
        foreach (var argument in arguments?.Items ?? [])
        {
            CollectUsed(argument.Value, used);
        }
    }

    static void CollectUsed(GraphQLDirectives? directives, HashSet<string> used)
    {
        foreach (var directive in directives?.Items ?? [])
        {
            CollectUsed(directive.Arguments, used);
        }
    }

    /// <summary>A variable can be nested at any depth inside a list or input object literal.</summary>
    static void CollectUsed(GraphQLValue value, HashSet<string> used)
    {
        switch (value)
        {
            case GraphQLVariable variable:
                used.Add(variable.Name.StringValue);
                break;

            case GraphQLListValue list:
                foreach (var item in list.Values ?? [])
                {
                    CollectUsed(item, used);
                }

                break;

            case GraphQLObjectValue objectValue:
                foreach (var field in objectValue.Fields ?? [])
                {
                    CollectUsed(field.Value, used);
                }

                break;
        }
    }
}
