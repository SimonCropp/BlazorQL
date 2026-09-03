namespace BlazorQL;

/// <summary>One completion suggestion, ready for a Monaco completion item.</summary>
public sealed record CompletionEntry(
    string Label,
    string Kind,
    string? Detail,
    string? Documentation,
    bool Deprecated,
    string SortText,
    string? InsertText = null);

/// <summary>
/// Schema-aware completion over the introspection model. The document is usually mid-edit and
/// unparseable, so context comes from a tolerant forward scan — brace and paren frames resolved
/// against the schema — rather than from an AST.
/// </summary>
public static class CompletionEngine
{
    public static IReadOnlyList<CompletionEntry> Complete(SchemaIndex schema, string text, int offset)
    {
        var scan = ContextScanner.Scan(schema, text, offset);
        return scan.Mode switch
        {
            ScanMode.Document => DocumentKeywords(),
            ScanMode.Selection => Fields(schema, scan),
            ScanMode.ArgumentName => ArgumentNames(scan),
            ScanMode.ArgumentValue => ArgumentValues(schema, scan),
            ScanMode.InputField => InputFields(scan),
            ScanMode.TypeCondition => CompositeTypes(schema),
            ScanMode.VariableType => InputTypes(schema),
            ScanMode.Variable => Variables(scan),
            ScanMode.Directive => Directives(schema),
            ScanMode.FragmentSpread => FragmentSpreads(schema, scan),
            _ => []
        };
    }

    static IReadOnlyList<CompletionEntry> DocumentKeywords() =>
    [
        new("query", "Keyword", null, null, false, "0"),
        new("mutation", "Keyword", null, null, false, "1"),
        new("subscription", "Keyword", null, null, false, "2"),
        new("fragment", "Keyword", null, null, false, "3"),
        new("{", "Keyword", null, null, false, "4"),
    ];

    static IReadOnlyList<CompletionEntry> Fields(SchemaIndex schema, ScanResult scan)
    {
        var type = scan.CurrentType;
        if (type is null)
        {
            return [];
        }

        var entries = new List<CompletionEntry>();
        var index = 0;
        foreach (var field in FieldsOf(schema, type))
        {
            entries.Add(new(
                field.Name,
                "Field",
                field.Type.Display(),
                field.Description,
                field.IsDeprecated,
                Sort(index++, field.Name)));
        }

        entries.Add(new("__typename", "Field", "String!", "The name of the object type at this point of the query.", false, Sort(index++, "__typename")));
        if (schema.IsRootType(type.Name) &&
            type.Name == schema.QueryTypeName)
        {
            entries.Add(new("__schema", "Field", "__Schema!", "The schema, exposed for introspection.", false, Sort(index++, "__schema")));
            entries.Add(new("__type", "Field", "__Type", "A named type, exposed for introspection.", false, Sort(index, "__type")));
        }

        return entries;
    }

    /// <summary>
    /// The fields reachable in a selection over <paramref name="type"/> — an interface or union
    /// offers nothing of its own beyond what the kind defines, but this keeps unions usable by
    /// offering their possible types indirectly through inline fragments elsewhere.
    /// </summary>
    static IEnumerable<IntrospectionField> FieldsOf(SchemaIndex schema, IntrospectionType type)
    {
        _ = schema;
        return type.Fields ?? [];
    }

    static IReadOnlyList<CompletionEntry> ArgumentNames(ScanResult scan)
    {
        // Inside a directive's parentheses the arguments are the directive's, not the field's.
        var declared = scan.CurrentDirective?.Args ?? scan.CurrentField?.Args;
        if (declared is null)
        {
            return [];
        }

        var index = 0;
        return
        [
            .. declared.Select(argument => new CompletionEntry(
                argument.Name,
                "Argument",
                argument.Type.Display(),
                argument.Description,
                argument.IsDeprecated,
                Sort(index++, argument.Name)))
        ];
    }

    static IReadOnlyList<CompletionEntry> ArgumentValues(SchemaIndex schema, ScanResult scan)
    {
        var entries = new List<CompletionEntry>();
        var type = scan.CurrentArgument is null
            ? null
            : schema.Find(scan.CurrentArgument.Type.Unwrap().Name);

        var index = 0;
        if (type?.Kind == "ENUM")
        {
            foreach (var value in type.EnumValues ?? [])
            {
                entries.Add(new(value.Name, "EnumMember", type.Name, value.Description, value.IsDeprecated, Sort(index++, value.Name)));
            }
        }

        if (type?.Name == "Boolean")
        {
            entries.Add(new("true", "Value", null, null, false, Sort(index++, "true")));
            entries.Add(new("false", "Value", null, null, false, Sort(index++, "false")));
        }

        foreach (var variable in scan.DeclaredVariables)
        {
            entries.Add(new($"${variable}", "Variable", null, null, false, Sort(index++, variable)));
        }

        return entries;
    }

    static IReadOnlyList<CompletionEntry> InputFields(ScanResult scan)
    {
        if (scan.CurrentInputType?.InputFields is not { } fields)
        {
            return [];
        }

        var index = 0;
        return
        [
            .. fields.Select(field => new CompletionEntry(
                field.Name,
                "Field",
                field.Type.Display(),
                field.Description,
                field.IsDeprecated,
                Sort(index++, field.Name)))
        ];
    }

    static IReadOnlyList<CompletionEntry> CompositeTypes(SchemaIndex schema) =>
        Types(schema, _ => _.Kind is "OBJECT" or "INTERFACE" or "UNION");

    static IReadOnlyList<CompletionEntry> InputTypes(SchemaIndex schema) =>
        Types(schema, _ => _.Kind is "SCALAR" or "ENUM" or "INPUT_OBJECT");

    static IReadOnlyList<CompletionEntry> Types(SchemaIndex schema, Func<IntrospectionType, bool> keep)
    {
        var index = 0;
        return
        [
            .. schema.Types
                .Where(_ => !_.Name.StartsWith("__", StringComparison.Ordinal) && keep(_))
                .Select(type => new CompletionEntry(
                    type.Name,
                    "Class",
                    type.Kind.ToLowerInvariant(),
                    type.Description,
                    false,
                    Sort(index++, type.Name)))
        ];
    }

    static IReadOnlyList<CompletionEntry> Variables(ScanResult scan)
    {
        var index = 0;
        return
        [
            .. scan.DeclaredVariables.Select(variable => new CompletionEntry(
                variable,
                "Variable",
                null,
                null,
                false,
                Sort(index++, variable)))
        ];
    }

    static IReadOnlyList<CompletionEntry> Directives(SchemaIndex schema)
    {
        var index = 0;
        return
        [
            .. schema.Directives.Select(directive => new CompletionEntry(
                directive.Name,
                "Interface",
                "directive",
                directive.Description,
                false,
                Sort(index++, directive.Name)))
        ];
    }

    static IReadOnlyList<CompletionEntry> FragmentSpreads(SchemaIndex schema, ScanResult scan)
    {
        var entries = new List<CompletionEntry>();
        var index = 0;
        foreach (var fragment in scan.FragmentNames)
        {
            entries.Add(new(fragment, "Reference", "fragment", null, false, Sort(index++, fragment)));
        }

        // "... on Type" is always available beside the named spreads.
        foreach (var entry in CompositeTypes(schema))
        {
            entries.Add(entry with
            {
                Label = $"on {entry.Label}",
                InsertText = $"on {entry.Label}",
                SortText = "z" + entry.SortText
            });
        }

        return entries;
    }

    // Declaration order, like GraphiQL: a numeric prefix keeps Monaco from re-sorting alphabetically.
    static string Sort(int index, string name) =>
        $"{index:d4}{name}";
}
