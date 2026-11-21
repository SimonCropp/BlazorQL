namespace BlazorQL;

/// <summary>
/// Deletes the field a GraphQL error points at, located by that error's response path.
/// </summary>
/// <remarks>
/// The path names response keys, so an aliased field matches on its alias, and list indices are no
/// part of it — a list's selection set is written once however many elements come back. Knock-on
/// edits keep the result a document the server will still accept. Removing the last selection in a
/// set would leave an empty pair of braces, so the removal takes what owns the set with it — the
/// parent field, the inline fragment, or the spread of the fragment. And what that leaves unused —
/// a variable the operation no longer reads, a fragment nothing spreads any more — is a validation
/// error in its own right, so it goes too.
/// </remarks>
public static class FieldRemover
{
    /// <summary>
    /// <paramref name="text"/> with the field at <paramref name="path"/> removed from the operation
    /// that ran, or null when there is no such removal to make: the operation is no longer in the
    /// document, the path does not resolve in it (a response left over from an edited query), or
    /// the field is the only thing the operation selects, and taking it out would leave an empty
    /// operation rather than a smaller one.
    /// </summary>
    /// <param name="operationName">
    /// The operation the run chose, null for a run that named none. A document with one operation
    /// can only have run that one, so the name only decides between several.
    /// </param>
    public static string? Remove(string text, IReadOnlyList<string> path, string? operationName = null)
    {
        if (path.Count == 0)
        {
            return null;
        }

        var info = DocumentInfo.Parse(text);
        if (info.Document is null ||
            Ran(info.Document, operationName) is not { } operation)
        {
            return null;
        }

        List<Step> chain = [];
        if (!Resolve(operation.SelectionSet, path, 0, Fragments(info.Document), chain, new(StringComparer.Ordinal)) ||
            Target(chain) is not { } target)
        {
            return null;
        }

        return RemoveOrphans(info.Document, Cut(text, target.Location.Start, target.Location.End));
    }

    /// <summary>
    /// The operation a response came from. The run named it when the document held several, and a
    /// run that named none ran the anonymous one. Any other operation selecting the same path is not
    /// what the error was about, so the path is never tried there.
    /// </summary>
    static GraphQLOperationDefinition? Ran(GraphQLDocument document, string? operationName)
    {
        var operations = document.Definitions.OfType<GraphQLOperationDefinition>().ToList();
        if (operations is [var only])
        {
            return only;
        }

        return operations.FirstOrDefault(_ => _.Name?.StringValue == operationName);
    }

    /// <summary>
    /// The fragments by name. Two of one name fail validation but parse, so the first is taken
    /// rather than thrown over.
    /// </summary>
    static Dictionary<string, GraphQLFragmentDefinition> Fragments(GraphQLDocument document)
    {
        var fragments = new Dictionary<string, GraphQLFragmentDefinition>(StringComparer.Ordinal);
        foreach (var fragment in document.Definitions.OfType<GraphQLFragmentDefinition>())
        {
            fragments.TryAdd(fragment.FragmentName.Name.StringValue, fragment);
        }

        return fragments;
    }

    /// <summary>
    /// One selection on the way down — a field, an inline fragment, or a fragment spread — with the
    /// set it was selected in.
    /// </summary>
    readonly record struct Step(GraphQLSelectionSet Set, ASTNode Selection);

    /// <summary>
    /// Walks the path through the document, recording the selections it passes. Fragments are
    /// followed: a path segment can perfectly well be satisfied by a field the query only mentions
    /// through a spread. They consume no segment, but they are steps all the same, because one can
    /// be what has to go. <paramref name="active"/> is the set of fragments already open on this
    /// branch, which is what stops a cycle from being followed forever.
    /// </summary>
    static bool Resolve(
        GraphQLSelectionSet? set,
        IReadOnlyList<string> path,
        int depth,
        IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments,
        List<Step> chain,
        HashSet<string> active)
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

                    if (Resolve(field.SelectionSet, path, depth + 1, fragments, chain, active))
                    {
                        return true;
                    }

                    // The name matched but nothing under it did, so this was the wrong branch.
                    chain.RemoveAt(chain.Count - 1);
                    continue;

                case GraphQLInlineFragment inline:
                    chain.Add(new(set, inline));
                    if (Resolve(inline.SelectionSet, path, depth, fragments, chain, active))
                    {
                        return true;
                    }

                    chain.RemoveAt(chain.Count - 1);
                    continue;

                case GraphQLFragmentSpread spread
                    when fragments.TryGetValue(spread.FragmentName.Name.StringValue, out var fragment):
                    // A spread does not consume a path segment, so a fragment that spreads its way
                    // back to itself would be re-entered forever. NoFragmentCycles is a deliberate
                    // validator gap, so such a document does reach here.
                    var name = spread.FragmentName.Name.StringValue;
                    if (!active.Add(name))
                    {
                        continue;
                    }

                    chain.Add(new(set, spread));
                    var found = Resolve(fragment.SelectionSet, path, depth, fragments, chain, active);
                    active.Remove(name);
                    if (found)
                    {
                        return true;
                    }

                    chain.RemoveAt(chain.Count - 1);
                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// The selection to actually cut. Removing the only selection in a set leaves braces around
    /// nothing, so that case climbs to whatever owns the set instead: the field it belongs to, the
    /// inline fragment it is, or the spread of the fragment it is the body of. Reaching the
    /// operation's own set means there is no removal that leaves a valid document.
    /// </summary>
    static ASTNode? Target(List<Step> chain)
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

        return chain[index].Selection;
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
    /// What a document's operations read: per operation, in document order, the variables it uses;
    /// and every fragment some operation reaches.
    /// </summary>
    sealed record Usage(
        List<HashSet<string>> Variables,
        HashSet<string> Reachable,
        Dictionary<string, GraphQLFragmentDefinition> Fragments);

    /// <summary>
    /// Follows each operation through the fragments it spreads, as the validation rules do: a
    /// variable belongs to its operation, so whether it is used is a question about the selections
    /// that operation reaches, its own directives included — not about the document as a whole.
    /// </summary>
    static Usage Read(GraphQLDocument document)
    {
        var fragments = Fragments(document);
        var variables = new List<HashSet<string>>();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in document.Definitions.OfType<GraphQLOperationDefinition>())
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var spread = new HashSet<string>(StringComparer.Ordinal);
            CollectUsed(operation.Directives, used);
            CollectReached(operation.SelectionSet, fragments, used, spread);
            variables.Add(used);
            reachable.UnionWith(spread);
        }

        return new(variables, reachable, fragments);
    }

    /// <summary>
    /// The variables a selection set reads, following its spreads into the fragments they name.
    /// <paramref name="spread"/> collects the fragments reached, and seeing one twice is what stops
    /// a cycle.
    /// </summary>
    static void CollectReached(
        GraphQLSelectionSet? set,
        IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments,
        HashSet<string> used,
        HashSet<string> spread)
    {
        foreach (var selection in set?.Selections ?? [])
        {
            switch (selection)
            {
                case GraphQLField field:
                    CollectUsed(field.Arguments, used);
                    CollectUsed(field.Directives, used);
                    CollectReached(field.SelectionSet, fragments, used, spread);
                    break;

                case GraphQLInlineFragment inline:
                    CollectUsed(inline.Directives, used);
                    CollectReached(inline.SelectionSet, fragments, used, spread);
                    break;

                case GraphQLFragmentSpread fragmentSpread:
                    CollectUsed(fragmentSpread.Directives, used);
                    var name = fragmentSpread.FragmentName.Name.StringValue;
                    if (spread.Add(name) &&
                        fragments.TryGetValue(name, out var fragment))
                    {
                        CollectReached(fragment.SelectionSet, fragments, used, spread);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Takes out what the cut orphaned: each variable an operation read before and no longer does,
    /// and each fragment some operation reached before and none does now. An unused variable fails
    /// the spec's NoUnusedVariables rule, and an unused fragment NoUnusedFragments, so leaving one
    /// behind would trade a failing field for a document the server rejects outright.
    /// </summary>
    /// <remarks>
    /// Only what the cut orphaned. A variable or fragment that was already unused is the user's to
    /// deal with — perhaps half way through an edit — and one in another operation is not what the
    /// click was about. Operations pair up by position: a cut never removes one.
    /// </remarks>
    static string RemoveOrphans(GraphQLDocument original, string text)
    {
        if (DocumentInfo.Parse(text).Document is not { } document)
        {
            return text;
        }

        var before = Read(original);
        var after = Read(document);
        List<(int Start, Func<string, string> Apply)> edits = [];

        var operations = document.Definitions.OfType<GraphQLOperationDefinition>().ToList();
        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i].Variables is not { Items.Count: > 0 } variables)
            {
                continue;
            }

            var orphaned = variables.Items
                .Where(_ => before.Variables[i].Contains(_.Variable.Name.StringValue) &&
                            !after.Variables[i].Contains(_.Variable.Name.StringValue))
                .ToList();

            // Emptying the list would leave "query Name()", which does not parse, so the whole
            // parenthesised block goes instead of its last member.
            if (orphaned.Count == variables.Items.Count)
            {
                edits.Add((variables.Location.Start, _ => _[..variables.Location.Start] + _[variables.Location.End..]));
                continue;
            }

            foreach (var variable in orphaned)
            {
                edits.Add((variable.Location.Start, _ => CutVariable(_, variable.Location.Start, variable.Location.End)));
            }
        }

        foreach (var (name, fragment) in after.Fragments)
        {
            if (before.Reachable.Contains(name) &&
                !after.Reachable.Contains(name))
            {
                edits.Add((fragment.Location.Start, _ => CutDefinition(_, fragment.Location.Start, fragment.Location.End)));
            }
        }

        // Right to left, so an earlier cut's offsets are still good after a later one.
        foreach (var edit in edits.OrderByDescending(_ => _.Start))
        {
            text = edit.Apply(text);
        }

        return text;
    }

    /// <summary>
    /// Cuts a whole definition with the blank lines after it — or, when nothing follows, with the
    /// ones before it — so the definitions either side keep the one blank line between them rather
    /// than growing a second. A definition that shares its line keeps the line's other content.
    /// </summary>
    static string CutDefinition(string text, int start, int end)
    {
        var to = end;
        while (to < text.Length &&
               char.IsWhiteSpace(text[to]))
        {
            to++;
        }

        var from = start;
        if (to == text.Length)
        {
            while (from > 0 &&
                   char.IsWhiteSpace(text[from - 1]))
            {
                from--;
            }

            // The document still ends the way it did.
            var ending = text.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" :
                text.EndsWith('\n') ? "\n" : "";
            return text[..from] + ending;
        }

        while (from > 0 &&
               text[from - 1] is ' ' or '\t')
        {
            from--;
        }

        if (from > 0 &&
            text[from - 1] != '\n')
        {
            from = start;
        }

        return text[..from] + text[to..];
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

    // The collectors are shared with QueryBuilder, which takes out the variables an edit orphaned.
    internal static void CollectUsed(GraphQLSelectionSet? set, HashSet<string> used)
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

    internal static void CollectUsed(GraphQLArguments? arguments, HashSet<string> used)
    {
        foreach (var argument in arguments?.Items ?? [])
        {
            CollectUsed(argument.Value, used);
        }
    }

    internal static void CollectUsed(GraphQLDirectives? directives, HashSet<string> used)
    {
        foreach (var directive in directives?.Items ?? [])
        {
            CollectUsed(directive.Arguments, used);
        }
    }

    /// <summary>A variable can be nested at any depth inside a list or input object literal.</summary>
    internal static void CollectUsed(GraphQLValue value, HashSet<string> used)
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
