namespace BlazorQL;

/// <summary>
/// The edits behind the query builder pane. Each takes the operation editor's text as it stands and
/// returns it with one change made, or null when the change no longer applies — the text has stopped
/// parsing, or the path no longer reaches anything.
/// </summary>
/// <remarks>
/// <para>
/// Every edit is a splice at the locations the parser reported, never a reprint. Printing the
/// definition back out would be simpler, but the printer only knows the comments the parser could
/// attach to a node — one written just before a closing brace belongs to nothing, and would vanish —
/// and it would reformat whatever had been laid out by hand, on every click. What the builder adds is
/// written in the formatter's layout; what was already there stays exactly as it was written.
/// </para>
/// <para>
/// A definition is addressed by its index among the document's definitions, so an operation and a
/// fragment are edited alike: a fragment is a selection set on its type condition, which is all the
/// builder needs a root for.
/// </para>
/// </remarks>
public static partial class QueryBuilder
{
    /// <summary>How far a default selection reaches before it settles for <c>__typename</c>.</summary>
    const int maxDepth = 3;

    const string typename = "__typename";

    // Prints values, which come out on one line; it never sees a comment.
    static readonly SDLPrinter printer = new();

    // ---- Reading: what the pane renders from ----

    /// <summary>The type a definition selects from: an operation's root type, a fragment's type condition.</summary>
    public static IntrospectionType? RootType(SchemaIndex schema, ASTNode definition) =>
        definition switch
        {
            GraphQLOperationDefinition operation => RootType(schema, operation.Operation),
            GraphQLFragmentDefinition fragment => schema.Find(fragment.TypeCondition.Type.Name.StringValue),
            _ => null
        };

    /// <summary>The root type an operation of the kind selects from, or null when the schema has none.</summary>
    public static IntrospectionType? RootType(SchemaIndex schema, OperationType operation) =>
        schema.Find(
            operation switch
            {
                OperationType.Mutation => schema.MutationTypeName,
                OperationType.Subscription => schema.SubscriptionTypeName,
                _ => schema.QueryTypeName
            });

    /// <summary>Whether a type takes a selection set.</summary>
    public static bool IsComposite(IntrospectionType? type) =>
        type?.Kind is "OBJECT" or "INTERFACE" or "UNION";

    /// <summary>
    /// The first field of the name the set selects, aliased or not. A field selected twice under two
    /// aliases reads as one checked box, and the first is the one the pane descends into.
    /// </summary>
    public static GraphQLField? Field(GraphQLSelectionSet? set, string name) =>
        Selections(set)
            .OfType<GraphQLField>()
            .FirstOrDefault(_ => _.Name.StringValue == name);

    /// <summary>The first inline fragment the set conditions on the type.</summary>
    public static GraphQLInlineFragment? InlineFragment(GraphQLSelectionSet? set, string typeName) =>
        Selections(set)
            .OfType<GraphQLInlineFragment>()
            .FirstOrDefault(_ => _.TypeCondition?.Type.Name.StringValue == typeName);

    /// <summary>Whether the set spreads the named fragment.</summary>
    public static bool Spreads(GraphQLSelectionSet? set, string fragmentName) =>
        Selections(set)
            .OfType<GraphQLFragmentSpread>()
            .Any(_ => _.FragmentName.Name.StringValue == fragmentName);

    public static GraphQLArgument? Argument(GraphQLField field, string name) =>
        field.Arguments?.Items.FirstOrDefault(_ => _.Name.StringValue == name);

    public static GraphQLObjectField? InputField(GraphQLObjectValue value, string name) =>
        value.Fields?.FirstOrDefault(_ => _.Name.StringValue == name);

    /// <summary>A value as GraphQL writes it.</summary>
    public static string Print(GraphQLValue value) =>
        printer.Print(value);

    /// <summary>A value as an input shows it: a string without its quotes, anything else as written.</summary>
    public static string Display(GraphQLValue value) =>
        value switch
        {
            GraphQLStringValue text => text.Value.ToString(),
            GraphQLNullValue => "",
            GraphQLListValue list => string.Join(", ", (list.Values ?? []).Select(Display)),
            _ => Print(value)
        };

    /// <summary>Whether the type is a list, nullable or not.</summary>
    public static bool IsList(TypeRef type) =>
        (type.Kind == "NON_NULL" ? type.OfType : type)?.Kind == "LIST";

    /// <summary>The type of a list type's items.</summary>
    static TypeRef ListItem(TypeRef type) =>
        (type.Kind == "NON_NULL" ? type.OfType! : type).OfType!;

    /// <summary>
    /// What an input's text means as a literal of <paramref name="type"/>, or null when it is not one.
    /// Every scalar but the numeric and boolean ones takes the text as a string, which is what an ID
    /// or a custom scalar such as a date nearly always travels as.
    /// </summary>
    public static GraphQLValue? Literal(SchemaIndex schema, TypeRef type, string input)
    {
        // A list is typed as its items separated by commas, each a literal of the item type.
        if (IsList(type))
        {
            var itemType = ListItem(type);
            List<GraphQLValue> items = [];
            foreach (var item in input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Literal(schema, itemType, item) is not { } literal)
                {
                    return null;
                }

                items.Add(literal);
            }

            return new GraphQLListValue
            {
                Values = items
            };
        }

        var named = schema.Find(type.Unwrap().Name);
        if (named?.Kind == "ENUM")
        {
            var value = input.Trim();
            if (named.EnumValues?.Any(_ => _.Name == value) == true)
            {
                return new GraphQLEnumValue(new(value));
            }

            return null;
        }

        if (named?.Kind != "SCALAR")
        {
            return null;
        }

        var trimmed = input.Trim();
        switch (named.Name)
        {
            case "Int":
                // In range as well as well formed: the validator refuses an Int past 32 bits.
                if (IntPattern().IsMatch(trimmed) &&
                    int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                {
                    return new GraphQLIntValue(trimmed);
                }

                return null;
            case "Float":
                if (FloatPattern().IsMatch(trimmed))
                {
                    return new GraphQLFloatValue(trimmed);
                }

                return null;
            case "Boolean":
                return trimmed switch
                {
                    "true" => new GraphQLTrueBooleanValue(),
                    "false" => new GraphQLFalseBooleanValue(),
                    _ => null
                };
            default:
                return new GraphQLStringValue(input);
        }
    }

    /// <summary>
    /// A placeholder literal for a value the builder supplies before anyone has typed one: the required
    /// arguments of a field it adds, and an argument or input field it switches on.
    /// </summary>
    /// <remarks>
    /// A list starts empty: a single item would pass input coercion, but reads as the wrong type to
    /// anyone looking at the operation. An input object carries its own required fields, so the literal is one the
    /// validator accepts as it stands.
    /// </remarks>
    public static GraphQLValue DefaultValue(SchemaIndex schema, TypeRef type) =>
        DefaultValue(schema, type, 0);

    /// <summary>The name a new operation of the kind takes: GraphiQL's, numbered past any already taken.</summary>
    public static string NewOperationName(GraphQLDocument? document, OperationType kind)
    {
        var stem = kind switch
        {
            OperationType.Mutation => "MyMutation",
            OperationType.Subscription => "MySubscription",
            _ => "MyQuery"
        };

        var taken = (document?.Definitions ?? [])
            .OfType<GraphQLOperationDefinition>()
            .Select(_ => _.Name?.StringValue)
            .ToHashSet(StringComparer.Ordinal);

        if (!taken.Contains(stem))
        {
            return stem;
        }

        var counter = 2;
        while (taken.Contains(stem + counter))
        {
            counter++;
        }

        return stem + counter;
    }

    // ---- Edits ----

    /// <summary>
    /// Selects the field or inline fragment the path ends in when nothing selects it, and takes it out
    /// when something does. A field goes in with the arguments it cannot do without and, when it has
    /// members of its own, the selection <see cref="LeafFiller"/> would have filled in, so the document
    /// runs as it stands. A field selected more than once, under different aliases, goes in one click.
    /// </summary>
    public static string? ToggleSelection(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path)
    {
        if (path.Count == 0 ||
            Parse(text, definition) is not { } root ||
            Locate(schema, root, path, path.Count - 1) is not { } chain)
        {
            return null;
        }

        var owner = chain[^1];
        var step = path[^1];
        List<ASTNode> existing = step.IsFragment
            ? [.. Selections(owner.Set).OfType<GraphQLInlineFragment>().Where(_ => _.TypeCondition?.Type.Name.StringValue == step.Name)]
            : [.. Selections(owner.Set).OfType<GraphQLField>().Where(_ => _.Name.StringValue == step.Name)];

        if (existing.Count > 0)
        {
            return Remove(text, definition, chain, existing);
        }

        ASTNode? selection = step.IsFragment
            ? NewInlineFragment(schema, step.Name)
            : NewField(schema, owner.Type, step.Name);

        if (selection is null)
        {
            return null;
        }

        return Apply(text, [Insert(text, chain[0], owner, selection)]);
    }

    /// <summary>Spreads a fragment into the set the path ends in, or takes the spread out again.</summary>
    public static string? ToggleSpread(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path, string fragment)
    {
        if (Parse(text, definition) is not { } root ||
            Locate(schema, root, path, path.Count) is not { } chain)
        {
            return null;
        }

        var owner = chain[^1];
        List<ASTNode> existing =
        [
            .. Selections(owner.Set)
                .OfType<GraphQLFragmentSpread>()
                .Where(_ => _.FragmentName.Name.StringValue == fragment)
        ];

        if (existing.Count > 0)
        {
            return Remove(text, definition, chain, existing);
        }

        return Apply(text, [Insert(text, chain[0], owner, new GraphQLFragmentSpread(new(new(fragment))))]);
    }

    /// <summary>
    /// Switches an argument of the field the path ends in on, with a placeholder value, or off. A longer
    /// input path reaches into an input object the argument already holds, and switches one of its
    /// fields instead.
    /// </summary>
    public static string? ToggleArgument(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path, IReadOnlyList<string> input)
    {
        if (Slot(schema, text, definition, path, input) is not { } slot)
        {
            return null;
        }

        if (slot.Value is null)
        {
            return Apply(text, [AddInput(text, slot, DefaultValue(schema, slot.Member.Type))]);
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        FieldRemover.CollectUsed(slot.Value, read);
        return DropOrphanedVariables(Apply(text, [RemoveInput(text, slot)]), definition, read);
    }

    /// <summary>
    /// Gives an argument, or a field of an input object one holds, the value — switching it on first
    /// when it was off. Null when the value is already the one written.
    /// </summary>
    public static string? SetArgument(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path, IReadOnlyList<string> input, GraphQLValue value)
    {
        if (Slot(schema, text, definition, path, input) is not { } slot)
        {
            return null;
        }

        if (slot.Value is null)
        {
            return Apply(text, [AddInput(text, slot, value)]);
        }

        var (start, end) = (slot.Value.Location.Start, slot.Value.Location.End);
        var printed = Print(value);
        if (text.AsSpan(start, end - start).SequenceEqual(printed))
        {
            return null;
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        FieldRemover.CollectUsed(slot.Value, read);
        return DropOrphanedVariables(Apply(text, [new(start, end, printed)]), definition, read);
    }

    /// <summary>
    /// Turns an argument's literal into a variable the operation declares, or a variable back into a
    /// literal. The literal survives the trip as the variable's default, so the operation asks exactly
    /// what it asked before until a value is supplied; going back, the variable's default becomes the
    /// literal again, or a placeholder where it has none.
    /// </summary>
    /// <remarks>Null for a fragment, which has nowhere to declare a variable.</remarks>
    public static string? ToggleVariable(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path, string argument)
    {
        if (Parse(text, definition) is not GraphQLOperationDefinition operation ||
            FieldAt(schema, operation, path) is not ({ } field, { } fieldDefinition) ||
            Argument(field, argument) is not { } node ||
            fieldDefinition.Args.FirstOrDefault(_ => _.Name == argument) is not { } member)
        {
            return null;
        }

        var value = node.Value;
        if (value is GraphQLVariable variable)
        {
            var name = variable.Name.StringValue;
            var declared = operation.Variables?.Items.FirstOrDefault(_ => _.Variable.Name.StringValue == name);
            var literal = Print(declared?.DefaultValue ?? DefaultValue(schema, member.Type));
            var restored = Apply(text, [new(value.Location.Start, value.Location.End, literal)]);
            return DropOrphanedVariables(restored, definition, [name]);
        }

        var variableName = VariableName(operation, field, argument);
        var declaration = $"${variableName}: {member.Type.Display()}";
        // A null default does not fit a non-null variable, so that one goes without.
        if (IsConstant(value) &&
            !(value is GraphQLNullValue && member.Type.Kind == "NON_NULL"))
        {
            declaration += $" = {Print(value)}";
        }

        return Apply(
            text,
            [
                new(value.Location.Start, value.Location.End, "$" + variableName),
                Declare(text, operation, declaration)
            ]);
    }

    /// <summary>
    /// Names an operation, or makes it anonymous again when the name is blank. Null when the name is not
    /// a GraphQL name, so a half-typed one leaves the document alone rather than breaking it.
    /// </summary>
    public static string? RenameOperation(string text, int definition, string name)
    {
        name = name.Trim();
        if ((name.Length > 0 && !NamePattern().IsMatch(name)) ||
            Parse(text, definition) is not GraphQLOperationDefinition operation)
        {
            return null;
        }

        if (operation.Name is { } existing)
        {
            if (name == existing.StringValue)
            {
                return null;
            }

            var (start, end) = (existing.Location.Start, existing.Location.End);
            if (name.Length > 0)
            {
                return Apply(text, [new(start, end, name)]);
            }

            // Anonymous again: the space that separated the name from what follows goes with it.
            if (end < text.Length &&
                text[end] == ' ')
            {
                end++;
            }

            return Apply(text, [new(start, end, "")]);
        }

        if (name.Length == 0)
        {
            return null;
        }

        var position = operation.Location.Start;
        // The shorthand query has no keyword to put the name after.
        if (text[position] == '{')
        {
            return Apply(text, [new(position, position, $"query {name} ")]);
        }

        var after = position + Keyword(operation.Operation).Length;
        return Apply(text, [new(after, after, " " + name)]);
    }

    /// <summary>Takes an operation out of the document. The comments written above it stay.</summary>
    public static string? RemoveOperation(string text, int definition) =>
        Parse(text, definition) is GraphQLOperationDefinition operation
            ? Apply(text, [RemoveDefinition(text, operation)])
            : null;

    /// <summary>
    /// Appends a new operation of the kind, selecting the field, after whatever the document already
    /// holds — the welcome comments included, which stay where they are.
    /// </summary>
    public static string? AddOperation(SchemaIndex schema, string text, OperationType kind, string field)
    {
        if (RootType(schema, kind) is not { } root ||
            NewField(schema, root, field) is not { } selection)
        {
            return null;
        }

        const string unit = "  ";
        var name = NewOperationName(DocumentInfo.Parse(text).Document, kind);
        var operation = $"{Keyword(kind)} {name} {{\n{unit}{Print(selection, unit, unit)}\n}}";
        var kept = text.TrimEnd();
        if (kept.Length == 0)
        {
            return operation + "\n";
        }

        return $"{kept}\n\n{operation}\n";
    }

    // ---- Locating what an edit addresses ----

    /// <summary>The definition an edit addresses, in the text as it now stands.</summary>
    static ASTNode? Parse(string text, int definition) =>
        DocumentInfo.Parse(text).Document?.Definitions.ElementAtOrDefault(definition) is { } node and (GraphQLOperationDefinition or GraphQLFragmentDefinition)
            ? node
            : null;

    static List<ASTNode> Selections(GraphQLSelectionSet? set) =>
        set?.Selections ?? [];

    /// <summary>Where a step landed: the node, and the type it selects from.</summary>
    sealed record Located(ASTNode Node, IntrospectionType? Type)
    {
        public GraphQLSelectionSet? Set =>
            Node switch
            {
                GraphQLOperationDefinition operation => operation.SelectionSet,
                GraphQLFragmentDefinition fragment => fragment.SelectionSet,
                GraphQLField selected => selected.SelectionSet,
                GraphQLInlineFragment inline => inline.SelectionSet,
                _ => null
            };
    }

    /// <summary>
    /// The first <paramref name="count"/> steps of the path, followed down from the definition. Null
    /// when a step finds nothing selected under its name.
    /// </summary>
    static List<Located>? Locate(SchemaIndex schema, ASTNode root, IReadOnlyList<BuilderStep> path, int count)
    {
        List<Located> chain = [new(root, RootType(schema, root))];
        for (var index = 0; index < count; index++)
        {
            var step = path[index];
            var current = chain[^1];
            if (step.IsFragment)
            {
                if (InlineFragment(current.Set, step.Name) is not { } inline)
                {
                    return null;
                }

                chain.Add(new(inline, schema.Find(step.Name)));
                continue;
            }

            if (Field(current.Set, step.Name) is not { } field)
            {
                return null;
            }

            chain.Add(new(field, schema.Find(schema.Field(current.Type, step.Name)?.Type.Unwrap().Name)));
        }

        return chain;
    }

    /// <summary>The field the path ends in, with the schema's definition of it.</summary>
    static (GraphQLField Field, IntrospectionField Definition)? FieldAt(SchemaIndex schema, ASTNode root, IReadOnlyList<BuilderStep> path)
    {
        if (path.Count == 0 ||
            path[^1].IsFragment ||
            Locate(schema, root, path, path.Count - 1) is not { } chain)
        {
            return null;
        }

        var owner = chain[^1];
        var name = path[^1].Name;
        if (Field(owner.Set, name) is not { } field ||
            schema.Field(owner.Type, name) is not { } definition)
        {
            return null;
        }

        return (field, definition);
    }

    /// <summary>
    /// Where an input path lands among a field's arguments: the input object holding it (none for an
    /// argument), the members the schema declares there, the one the path names, and its current node.
    /// </summary>
    sealed record InputSlot(
        GraphQLField Field,
        GraphQLObjectValue? Container,
        IReadOnlyList<IntrospectionInputValue> Members,
        IntrospectionInputValue Member,
        ASTNode? Node,
        GraphQLValue? Value);

    static InputSlot? Slot(SchemaIndex schema, string text, int definition, IReadOnlyList<BuilderStep> path, IReadOnlyList<string> input)
    {
        if (input.Count == 0 ||
            Parse(text, definition) is not { } root ||
            FieldAt(schema, root, path) is not ({ } field, { } fieldDefinition))
        {
            return null;
        }

        var members = fieldDefinition.Args;
        GraphQLObjectValue? container = null;
        for (var index = 0;; index++)
        {
            var name = input[index];
            if (members.FirstOrDefault(_ => _.Name == name) is not { } member)
            {
                return null;
            }

            ASTNode? node = container is null
                ? Argument(field, name)
                : InputField(container, name);
            var value = node switch
            {
                GraphQLArgument argument => argument.Value,
                GraphQLObjectField objectField => objectField.Value,
                _ => null
            };

            if (index == input.Count - 1)
            {
                return new(field, container, members, member, node, value);
            }

            // Only a value written as an object can be reached into; a variable or a null stands for
            // the whole of it.
            if (value is not GraphQLObjectValue nested ||
                schema.Find(member.Type.Unwrap().Name) is not {Kind: "INPUT_OBJECT"} inputType)
            {
                return null;
            }

            members = inputType.InputFields ?? [];
            container = nested;
        }
    }

    // ---- Building what an edit adds ----

    static IEnumerable<IntrospectionInputValue> Required(IReadOnlyList<IntrospectionInputValue>? values) =>
        (values ?? []).Where(_ => _ is {IsDeprecated: false, Type.Kind: "NON_NULL", DefaultValue: null});

    static GraphQLValue DefaultValue(SchemaIndex schema, TypeRef type, int depth)
    {
        if (IsList(type))
        {
            return new GraphQLListValue
            {
                Values = []
            };
        }

        var named = schema.Find(type.Unwrap().Name);
        switch (named?.Kind)
        {
            case "ENUM":
                var values = named.EnumValues ?? [];
                if (values.Count == 0)
                {
                    return new GraphQLNullValue();
                }

                var first = values.FirstOrDefault(_ => !_.IsDeprecated) ?? values[0];

                return new GraphQLEnumValue(new(first.Name));
            case "INPUT_OBJECT":
                List<GraphQLObjectField> fields = [];
                if (depth < maxDepth)
                {
                    fields.AddRange(Required(named.InputFields).Select(_ => new GraphQLObjectField(new(_.Name), DefaultValue(schema, _.Type, depth + 1))));
                }

                return new GraphQLObjectValue
                {
                    Fields = fields
                };
        }

        return named?.Name switch
        {
            "Int" => new GraphQLIntValue(0),
            "Float" => new GraphQLFloatValue("0.0"),
            "Boolean" => new GraphQLFalseBooleanValue(),
            _ => new GraphQLStringValue("")
        };
    }

    static GraphQLField? NewField(SchemaIndex schema, IntrospectionType? owner, string name)
    {
        if (name == typename)
        {
            return new(new(typename));
        }

        if (schema.Field(owner, name) is not { } definition)
        {
            return null;
        }

        return NewField(schema, definition, 1);
    }

    static GraphQLField NewField(SchemaIndex schema, IntrospectionField definition, int depth)
    {
        var field = new GraphQLField(new(definition.Name));
        var required = Required(definition.Args).ToList();
        if (required.Count > 0)
        {
            field.Arguments = new([.. required.Select(_ => new GraphQLArgument(new(_.Name), DefaultValue(schema, _.Type, 0)))]);
        }

        var type = schema.Find(definition.Type.Unwrap().Name);
        if (IsComposite(type))
        {
            field.SelectionSet = DefaultSelection(schema, type!, depth);
        }

        return field;
    }

    static GraphQLInlineFragment? NewInlineFragment(SchemaIndex schema, string typeName)
    {
        if (schema.Find(typeName) is not { } type ||
            !IsComposite(type))
        {
            return null;
        }

        return new(DefaultSelection(schema, type, 1))
        {
            TypeCondition = new(new(new(typeName)))
        };
    }

    /// <summary>
    /// What a newly selected composite field selects: <see cref="LeafFiller"/>'s default choice, nested
    /// as far as <see cref="maxDepth"/>, or <c>__typename</c> where that choice is empty — a union has
    /// no fields of its own, and braces around nothing do not parse.
    /// </summary>
    static GraphQLSelectionSet DefaultSelection(SchemaIndex schema, IntrospectionType type, int depth)
    {
        List<ASTNode> selections = [];
        // The same reach LeafFiller has: a connection's edges, their node, and the node's id.
        if (depth <= maxDepth)
        {
            foreach (var name in LeafFiller.DefaultFieldNames(type))
            {
                if (schema.Field(type, name) is { } definition)
                {
                    selections.Add(NewField(schema, definition, depth + 1));
                }
            }
        }

        if (selections.Count == 0)
        {
            selections.Add(new GraphQLField(new(typename)));
        }

        return new(selections);
    }

    static bool IsConstant(GraphQLValue value) =>
        value switch
        {
            GraphQLVariable => false,
            GraphQLListValue list => (list.Values ?? []).All(IsConstant),
            GraphQLObjectValue objectValue => (objectValue.Fields ?? []).All(_ => IsConstant(_.Value)),
            _ => true
        };

    /// <summary>
    /// A variable for an argument: the argument's own name, the field's joined to it when that is taken,
    /// and a counter after that — the rule <see cref="QueryGenerator"/> names its variables by.
    /// </summary>
    static string VariableName(GraphQLOperationDefinition operation, GraphQLField field, string argument)
    {
        var taken = (operation.Variables?.Items ?? [])
            .Select(_ => _.Variable.Name.StringValue)
            .ToHashSet(StringComparer.Ordinal);

        var name = argument;
        if (taken.Contains(name))
        {
            name = field.Name.StringValue + char.ToUpperInvariant(argument[0]) + argument[1..];
        }

        var candidate = name;
        var counter = 2;
        while (taken.Contains(candidate))
        {
            candidate = name + counter++;
        }

        return candidate;
    }

    static string Keyword(OperationType kind) =>
        kind switch
        {
            OperationType.Mutation => "mutation",
            OperationType.Subscription => "subscription",
            _ => "query"
        };

    // ---- Writing a selection ----

    /// <summary>A new selection, laid out the way the formatter would, its first line placed by the caller.</summary>
    static string Print(ASTNode selection, string indent, string unit)
    {
        var builder = new StringBuilder();
        AppendSelection(builder, selection, indent, unit, compact: false);
        return builder.ToString();
    }

    /// <summary>A new selection on one line, for a set written on one line.</summary>
    static string Compact(ASTNode selection)
    {
        var builder = new StringBuilder();
        AppendSelection(builder, selection, "", "", compact: true);
        return builder.ToString();
    }

    static void AppendSelection(StringBuilder builder, ASTNode selection, string indent, string unit, bool compact)
    {
        switch (selection)
        {
            case GraphQLField field:
                builder.Append(field.Name.StringValue);
                if (field.Arguments is {Items.Count: > 0} arguments)
                {
                    builder.Append('(');
                    builder.AppendJoin(", ", arguments.Items.Select(_ => $"{_.Name.StringValue}: {Print(_.Value)}"));
                    builder.Append(')');
                }

                AppendSet(builder, field.SelectionSet, indent, unit, compact);
                break;
            case GraphQLInlineFragment inline:
                builder.Append("... on ");
                builder.Append(inline.TypeCondition?.Type.Name.StringValue);
                AppendSet(builder, inline.SelectionSet, indent, unit, compact);
                break;
            case GraphQLFragmentSpread spread:
                builder.Append("...");
                builder.Append(spread.FragmentName.Name.StringValue);
                break;
        }
    }

    static void AppendSet(StringBuilder builder, GraphQLSelectionSet? set, string indent, string unit, bool compact)
    {
        if (set is null)
        {
            return;
        }

        if (compact)
        {
            builder.Append(" {");
            foreach (var child in set.Selections)
            {
                builder.Append(' ');
                AppendSelection(builder, child, indent, unit, compact);
            }

            builder.Append(" }");
            return;
        }

        var inner = indent + unit;
        builder.Append(" {\n");
        foreach (var child in set.Selections)
        {
            builder.Append(inner);
            AppendSelection(builder, child, inner, unit, compact);
            builder.Append('\n');
        }

        builder.Append(indent);
        builder.Append('}');
    }

    // ---- Splicing ----

    /// <summary>One span of the text and what replaces it.</summary>
    readonly record struct Splice(int Start, int End, string Text);

    /// <summary>
    /// Makes the splices, last first so the earlier offsets still hold. Two deletions that meet — a
    /// selection cut with the spaces after it, beside one cut with the spaces before it — are made as
    /// one.
    /// </summary>
    static string Apply(string text, IEnumerable<Splice> splices)
    {
        List<Splice> merged = [];
        foreach (var splice in splices.OrderBy(_ => _.Start).ThenBy(_ => _.End))
        {
            if (merged.Count > 0 &&
                merged[^1] is {Text.Length: 0} previous &&
                splice.Text.Length == 0 &&
                splice.Start <= previous.End)
            {
                merged[^1] = previous with
                {
                    End = Math.Max(previous.End, splice.End)
                };
                continue;
            }

            merged.Add(splice);
        }

        var builder = new StringBuilder(text);
        for (var index = merged.Count - 1; index >= 0; index--)
        {
            var splice = merged[index];
            builder.Remove(splice.Start, splice.End - splice.Start);
            builder.Insert(splice.Start, splice.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Takes nodes out of the set the chain ends in, and then the declarations of the variables only
    /// they read.
    /// </summary>
    static string Remove(string text, int definition, List<Located> chain, IReadOnlyList<ASTNode> nodes)
    {
        var read = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            CollectUsed(node, read);
        }

        var edited = Apply(text, RemoveSplices(text, chain, nodes, out var removedDefinition));
        // A definition taken out whole took its declarations with it, and its index now names another.
        if (removedDefinition)
        {
            return edited;
        }

        return DropOrphanedVariables(edited, definition, read);
    }

    static void CollectUsed(ASTNode node, HashSet<string> read)
    {
        switch (node)
        {
            case GraphQLField field:
                FieldRemover.CollectUsed(field.Arguments, read);
                FieldRemover.CollectUsed(field.Directives, read);
                FieldRemover.CollectUsed(field.SelectionSet, read);
                break;
            case GraphQLInlineFragment inline:
                FieldRemover.CollectUsed(inline.Directives, read);
                FieldRemover.CollectUsed(inline.SelectionSet, read);
                break;
            case GraphQLFragmentSpread spread:
                FieldRemover.CollectUsed(spread.Directives, read);
                break;
        }
    }

    /// <summary>
    /// The splices that take nodes out of the set the chain ends in. Emptying a set cannot leave its
    /// braces behind, which do not parse: an emptied field is left bare — it was chosen, only its
    /// members were not, the validator says what it lacks, and running fills it in — an emptied inline
    /// fragment goes with what it held, and an emptied operation or fragment goes entirely.
    /// </summary>
    static List<Splice> RemoveSplices(string text, List<Located> chain, IReadOnlyList<ASTNode> nodes, out bool removedDefinition)
    {
        removedDefinition = false;
        var owner = chain[^1];
        var set = owner.Set!;
        if (nodes.Count < set.Selections.Count)
        {
            return [.. nodes.Select(_ => Cut(text, StartOf(_), _.Location.End))];
        }

        switch (owner.Node)
        {
            case GraphQLField field:
                var start = set.Location.Start;
                while (start > field.Location.Start &&
                       char.IsWhiteSpace(text[start - 1]))
                {
                    start--;
                }

                return [new(start, set.Location.End, "")];
            case GraphQLInlineFragment inline:
                return RemoveSplices(text, chain[..^1], [inline], out removedDefinition);
            default:
                removedDefinition = true;
                return [RemoveDefinition(text, owner.Node)];
        }
    }

    /// <summary>Where a node starts, counting the comments written above it, which go wherever it goes.</summary>
    static int StartOf(ASTNode node)
    {
        var start = node.Location.Start;
        foreach (var comment in node.Comments ?? [])
        {
            start = Math.Min(start, comment.Location.Start);
        }

        return start;
    }

    /// <summary>
    /// Removes a span, taking its line with it when it had the line to itself — the indentation before
    /// it and the newline after — so a removal never leaves a blank line behind. Sharing its line, it
    /// takes the separator after it instead, or before it at the end of the line, so the neighbours stay
    /// one space apart.
    /// </summary>
    static Splice Cut(string text, int start, int end)
    {
        var after = end;
        while (after < text.Length &&
               text[after] is ' ' or '\t' or ',')
        {
            after++;
        }

        var endsLine = after == text.Length || text[after] is '\r' or '\n';
        if (endsLine &&
            StartsLine(text, start))
        {
            return new(LineStart(text, start), NextLine(text, after), "");
        }

        if (endsLine)
        {
            while (start > 0 &&
                   text[start - 1] is ' ' or '\t' or ',')
            {
                start--;
            }
        }

        return new(start, after, "");
    }

    /// <summary>
    /// Takes a whole definition out, with the blank line that separated it from the next, so removing
    /// one from between two others leaves them a blank line apart as they were. The comments above it
    /// stay: they may be the welcome text, or notes about the document rather than this definition.
    /// </summary>
    static Splice RemoveDefinition(string text, ASTNode definition)
    {
        var (start, end) = (definition.Location.Start, definition.Location.End);
        var after = end;
        while (after < text.Length &&
               text[after] is ' ' or '\t')
        {
            after++;
        }

        if (!StartsLine(text, start) ||
            (after < text.Length && text[after] is not ('\r' or '\n')))
        {
            return Cut(text, start, end);
        }

        var from = LineStart(text, start);
        var to = NextLine(text, after);
        if (to < text.Length &&
            IsBlankLine(text, to))
        {
            to = NextLine(text, to);
        }
        else if (to == text.Length &&
                 from > 0 &&
                 IsBlankLine(text, LineStart(text, from - 1)))
        {
            // The last definition: the blank line before it would be left trailing the document.
            from = LineStart(text, from - 1);
        }

        return new(from, to, "");
    }

    /// <summary>
    /// The splice that adds a selection to the owner's set, in schema order and written the way the
    /// set around it already is: on a line of its own where the selections are one to a line, and in
    /// line where the set is. A bare field takes braces around it.
    /// </summary>
    static Splice Insert(string text, Located root, Located owner, ASTNode selection)
    {
        var unit = Unit(text, root);
        var set = owner.Set;
        if (set is null)
        {
            var (start, end) = (owner.Node.Location.Start, owner.Node.Location.End);
            if (!StartsLine(text, start))
            {
                return new(end, end, $" {{ {Compact(selection)} }}");
            }

            var indent = Indentation(text, start);
            var inner = indent + unit;
            return new(end, end, $" {{\n{inner}{Print(selection, inner, unit)}\n{indent}}}");
        }

        var order = Order(owner.Type, selection);
        var anchor = order is null
            ? null
            : set.Selections.FirstOrDefault(_ => Order(owner.Type, _) > order);

        if (SameLine(text, set.Location.Start, set.Location.End - 1))
        {
            if (anchor is not null)
            {
                return new(anchor.Location.Start, anchor.Location.Start, Compact(selection) + " ");
            }

            var lastEnd = set.Selections[^1].Location.End;
            return new(lastEnd, lastEnd, " " + Compact(selection));
        }

        var childIndent = ChildIndentation(text, owner.Node, set, unit);
        if (anchor is not null)
        {
            var anchorStart = StartOf(anchor);
            if (!StartsLine(text, anchorStart))
            {
                return new(anchor.Location.Start, anchor.Location.Start, Compact(selection) + " ");
            }

            var anchorLine = LineStart(text, anchorStart);
            return new(anchorLine, anchorLine, childIndent + Print(selection, childIndent, unit) + "\n");
        }

        // Ahead of the closing brace's line rather than straight after the last selection, which may
        // carry a comment on the rest of its line.
        var close = set.Location.End - 1;
        if (StartsLine(text, close))
        {
            var closeLine = LineStart(text, close);
            return new(closeLine, closeLine, childIndent + Print(selection, childIndent, unit) + "\n");
        }

        var last = set.Selections[^1].Location.End;
        return new(last, last, "\n" + childIndent + Print(selection, childIndent, unit));
    }

    /// <summary>
    /// Where a selection sorts among its siblings: fields in the order the schema declares them,
    /// <c>__typename</c> after them, inline fragments after that in the order the possible types are
    /// listed, and spreads last. Null for a field the schema does not know, which keeps its place
    /// without deciding where anything else goes.
    /// </summary>
    static int? Order(IntrospectionType? type, ASTNode selection)
    {
        var fields = type?.Fields ?? [];
        switch (selection)
        {
            case GraphQLField field:
                var name = field.Name.StringValue;
                if (name == typename)
                {
                    return fields.Count;
                }

                var index = IndexOf(fields, _ => _.Name, name);
                return index < 0 ? null : index;
            case GraphQLInlineFragment inline:
                var possible = IndexOf(type?.PossibleTypes ?? [], _ => _.Name, inline.TypeCondition?.Type.Name.StringValue);
                return fields.Count + 1 + Math.Max(possible, 0);
            case GraphQLFragmentSpread:
                return int.MaxValue;
            default:
                return null;
        }
    }

    static int IndexOf<T>(IReadOnlyList<T> items, Func<T, string?> name, string? value)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (name(items[index]) == value)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Adds an argument or an input field, beside its siblings in the order the schema declares them.
    /// The first one gives the field its parentheses, or an empty object its first member.
    /// </summary>
    static Splice AddInput(string text, InputSlot slot, GraphQLValue value)
    {
        var item = $"{slot.Member.Name}: {Print(value)}";
        var order = IndexOf(slot.Members, _ => _.Name, slot.Member.Name);
        if (slot.Container is null)
        {
            if (slot.Field.Arguments is not {Items.Count: > 0} arguments)
            {
                var end = slot.Field.Name.Location.End;
                return new(end, end, $"({item})");
            }

            return InsertItem(text, arguments.Items, _ => IndexOf(slot.Members, member => member.Name, _.Name.StringValue), order, item);
        }

        if (slot.Container.Fields is not {Count: > 0} fields)
        {
            return new(slot.Container.Location.Start, slot.Container.Location.End, $"{{{item}}}");
        }

        return InsertItem(text, fields, _ => IndexOf(slot.Members, member => member.Name, _.Name.StringValue), order, item);
    }

    /// <summary>
    /// Takes an argument or an input field out. The last argument takes the parentheses with it,
    /// which do not parse empty; the last field of an object leaves the object empty, which does.
    /// </summary>
    static Splice RemoveInput(string text, InputSlot slot)
    {
        var node = slot.Node!;
        if (slot.Container is null)
        {
            var arguments = slot.Field.Arguments!;
            if (arguments.Items.Count == 1)
            {
                return new(arguments.Location.Start, arguments.Location.End, "");
            }

            return CutItem(text, node, ReferenceEquals(arguments.Items[^1], node));
        }

        var fields = slot.Container.Fields!;
        if (fields.Count == 1)
        {
            return new(slot.Container.Location.Start, slot.Container.Location.End, "{}");
        }

        return CutItem(text, node, ReferenceEquals(fields[^1], node));
    }

    /// <summary>
    /// Adds an item to a comma-separated list — an argument, an input field, a variable — before the
    /// first sibling the order puts after it, and written the way the list already is: on a line of its
    /// own where the items are one to a line, after a comma where they share one.
    /// </summary>
    static Splice InsertItem<T>(string text, IReadOnlyList<T> items, Func<T, int> orderOf, int order, string item)
        where T : ASTNode
    {
        var anchor = order < 0
            ? null
            : items.FirstOrDefault(_ => orderOf(_) > order);

        if (anchor is not null)
        {
            var start = anchor.Location.Start;
            var separator = StartsLine(text, start)
                ? "\n" + Indentation(text, start)
                : ", ";
            return new(start, start, item + separator);
        }

        var last = items[^1];
        var end = last.Location.End;
        if (StartsLine(text, last.Location.Start))
        {
            return new(end, end, "\n" + Indentation(text, last.Location.Start) + item);
        }

        return new(end, end, ", " + item);
    }

    /// <summary>
    /// Takes one item out of a comma-separated list with the separator that joined it to a neighbour:
    /// the one after it, or for the last item the one before.
    /// </summary>
    static Splice CutItem(string text, ASTNode node, bool last)
    {
        var (start, end) = (node.Location.Start, node.Location.End);
        if (!last)
        {
            while (end < text.Length &&
                   text[end] is ' ' or '\t' or '\r' or '\n' or ',')
            {
                end++;
            }

            return new(start, end, "");
        }

        while (start > 0 &&
               text[start - 1] is ' ' or '\t' or '\r' or '\n' or ',')
        {
            start--;
        }

        return new(start, end, "");
    }

    /// <summary>Declares a variable on the operation, giving it the parentheses when it has none.</summary>
    static Splice Declare(string text, GraphQLOperationDefinition operation, string declaration)
    {
        if (operation.Variables is {Items.Count: > 0} variables)
        {
            return InsertItem(text, variables.Items, _ => -1, -1, declaration);
        }

        if (operation.Name is { } name)
        {
            return new(name.Location.End, name.Location.End, $"({declaration})");
        }

        var start = operation.Location.Start;
        // The shorthand query takes the keyword it needs to carry a variable.
        if (text[start] == '{')
        {
            return new(start, start, $"query ({declaration}) ");
        }

        var after = start + Keyword(operation.Operation).Length;
        return new(after, after, $"({declaration})");
    }

    /// <summary>
    /// Takes out the declarations of the variables an edit took the last use of. Only those: one
    /// written ahead of the field that will read it is work in progress rather than an orphan, and is
    /// none of the builder's business. A use in any fragment counts, since the operation may spread it.
    /// </summary>
    static string DropOrphanedVariables(string text, int definition, IReadOnlyCollection<string> names)
    {
        // One at a time, re-parsed in between: two neighbours cut together would each claim the
        // separator between them.
        foreach (var name in names)
        {
            text = DropOrphanedVariable(text, definition, name);
        }

        return text;
    }

    static string DropOrphanedVariable(string text, int definition, string name)
    {
        if (DocumentInfo.Parse(text).Document is not { } document ||
            document.Definitions.ElementAtOrDefault(definition) is not GraphQLOperationDefinition {Variables: { } variables} operation ||
            variables.Items.FirstOrDefault(_ => _.Variable.Name.StringValue == name) is not { } declaration)
        {
            return text;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        FieldRemover.CollectUsed(operation.SelectionSet, used);
        FieldRemover.CollectUsed(operation.Directives, used);
        foreach (var fragment in document.Definitions.OfType<GraphQLFragmentDefinition>())
        {
            FieldRemover.CollectUsed(fragment.SelectionSet, used);
        }

        if (used.Contains(name))
        {
            return text;
        }

        if (variables.Items.Count > 1)
        {
            return Apply(text, [CutItem(text, declaration, ReferenceEquals(variables.Items[^1], declaration))]);
        }

        var (start, end) = (variables.Location.Start, variables.Location.End);
        // `query ($a: Int) {` would otherwise keep a space either side of where the list was.
        if (start > 0 &&
            text[start - 1] == ' ' &&
            end < text.Length &&
            text[end] == ' ')
        {
            start--;
        }

        return Apply(text, [new(start, end, "")]);
    }

    // ---- Lines ----

    /// <summary>
    /// The indentation one level adds, read off the definition's first selection where it sits on a
    /// line of its own — so four spaces stay four — and the formatter's two spaces otherwise.
    /// </summary>
    static string Unit(string text, Located root)
    {
        if (root.Set is {Selections.Count: > 0} set)
        {
            var first = StartOf(set.Selections[0]);
            if (StartsLine(text, first))
            {
                var outer = Indentation(text, root.Node.Location.Start);
                var inner = Indentation(text, first);
                if (inner.Length > outer.Length &&
                    inner.StartsWith(outer, StringComparison.Ordinal))
                {
                    return inner[outer.Length..];
                }
            }
        }

        return "  ";
    }

    /// <summary>The indentation of the set's selections: the first one on a line of its own, else one level in.</summary>
    static string ChildIndentation(string text, ASTNode owner, GraphQLSelectionSet set, string unit)
    {
        foreach (var selection in set.Selections)
        {
            var start = StartOf(selection);
            if (StartsLine(text, start))
            {
                return Indentation(text, start);
            }
        }

        return Indentation(text, owner.Location.Start) + unit;
    }

    static int LineStart(string text, int offset)
    {
        while (offset > 0 &&
               text[offset - 1] != '\n')
        {
            offset--;
        }

        return offset;
    }

    /// <summary>The offset just past the newline that ends the line the offset is on, or the end of the text.</summary>
    static int NextLine(string text, int offset)
    {
        var newline = text.IndexOf('\n', offset);
        return newline < 0 ? text.Length : newline + 1;
    }

    /// <summary>Whether nothing but indentation comes before the offset on its line.</summary>
    static bool StartsLine(string text, int offset)
    {
        for (var index = LineStart(text, offset); index < offset; index++)
        {
            if (text[index] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    static bool IsBlankLine(string text, int lineStart) =>
        text.AsSpan(lineStart, NextLine(text, lineStart) - lineStart).IsWhiteSpace();

    static bool SameLine(string text, int start, int end) =>
        !text.AsSpan(start, end - start).Contains('\n');

    static string Indentation(string text, int offset)
    {
        var start = LineStart(text, offset);
        var end = start;
        while (end < text.Length &&
               text[end] is ' ' or '\t')
        {
            end++;
        }

        return text[start..end];
    }

    [GeneratedRegex("^-?(0|[1-9][0-9]*)$")]
    private static partial Regex IntPattern();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$")]
    private static partial Regex FloatPattern();

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex NamePattern();
}
