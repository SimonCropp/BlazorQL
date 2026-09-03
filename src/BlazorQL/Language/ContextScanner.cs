namespace BlazorQL;

public enum ScanMode
{
    None,
    Document,
    Selection,
    ArgumentName,
    ArgumentValue,
    InputField,
    TypeCondition,
    VariableType,
    Variable,
    Directive,
    FragmentSpread
}

/// <summary>What the scanner resolved at the caret.</summary>
public sealed record ScanResult(
    ScanMode Mode,
    IntrospectionType? CurrentType,
    IntrospectionField? CurrentField,
    IntrospectionInputValue? CurrentArgument,
    IntrospectionType? CurrentInputType,
    IReadOnlyList<string> DeclaredVariables,
    IReadOnlyList<string> FragmentNames,
    /// <summary>
    /// Set when the caret is inside a directive's argument list, where the arguments on offer are
    /// the directive's own and not the enclosing field's.
    /// </summary>
    IntrospectionDirective? CurrentDirective = null);

/// <summary>
/// A tolerant forward scan of a GraphQL document up to the caret. Completion runs mid-edit, on
/// text that rarely parses, so context comes from brace/paren frames resolved live against the
/// schema rather than from an AST. Strings and comments are skipped whole; anything unrecognized
/// is ignored and the scan carries on.
/// </summary>
static class ContextScanner
{
    /// <summary>
    /// Stands in for a root operation type the schema does not define — a mutation against a
    /// query-only schema, say. Distinct from null, which means nothing is pending and lets the
    /// anonymous shorthand fall back to the query root; no type can be named this, so the lookup
    /// finds nothing and the selection offers nothing.
    /// </summary>
    const string missingRoot = "\0";

    enum FrameKind
    {
        Selection,
        Arguments,
        InputObject,
        List
    }

    /// <summary>
    /// A directive name just consumed, carrying the schema's definition of it when there is one. An
    /// unknown directive still gets a frame: its arguments are unknown, which is not at all the
    /// same as the enclosing field's.
    /// </summary>
    readonly record struct PendingDirective(IntrospectionDirective? Directive);

    sealed class Frame(FrameKind kind)
    {
        public FrameKind Kind { get; } = kind;
        public IntrospectionType? Type { get; set; }
        public IntrospectionField? LastField { get; set; }

        /// <summary>Set instead of <see cref="LastField"/> for a directive's argument list.</summary>
        public IntrospectionDirective? Directive { get; set; }

        public IntrospectionInputValue? CurrentArgument { get; set; }
        public IntrospectionInputValue? CurrentInputField { get; set; }
        public bool AfterColon { get; set; }

        /// <summary>What an argument name in this frame is looked up in.</summary>
        public IReadOnlyList<IntrospectionInputValue> Declared =>
            Directive?.Args ?? LastField?.Args ?? [];
    }

    public static ScanResult Scan(SchemaIndex schema, string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var frames = new Stack<Frame>();
        var variables = new List<string>();
        var fragments = new List<string>();

        // What the token BEFORE the caret's word was — decides the modes a bare name cannot.
        string? pendingRoot = null;
        var afterEllipsis = false;
        var afterOn = false;
        var afterAt = false;
        var afterDollar = false;
        var inVariableDefinitions = false;
        var variableAwaitingType = false;

        // Set while the last token consumed was a directive name, so a "(" opens that directive's
        // arguments and not the enclosing field's.
        PendingDirective? pendingDirective = null;

        // Fragment names come from the whole document, not only the part before the caret.
        CollectFragments(text, fragments);

        var i = 0;
        while (i < offset)
        {
            var ch = text[i];

            if (ch == '#')
            {
                while (i < offset && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (ch == '"')
            {
                i = SkipString(text, i, offset);
                // A string that ends at the caret is the literal being typed, not a finished value.
                if (i < offset)
                {
                    ValueDone(frames);
                }

                continue;
            }

            if (char.IsWhiteSpace(ch) || ch == ',')
            {
                i++;
                continue;
            }

            if (ch == '.' && i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
            {
                afterEllipsis = true;
                afterOn = false;
                i += 3;
                continue;
            }

            if (ch == '$')
            {
                afterDollar = true;
                i++;
                continue;
            }

            if (ch == '@')
            {
                afterAt = true;
                i++;
                continue;
            }

            if (char.IsDigit(ch) || ch == '-')
            {
                // Int and Float literals, exponent and sign included.
                while (i < text.Length &&
                       (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '+' or '-'))
                {
                    i++;
                }

                // A number whose end is the caret is the literal being typed, not a finished value.
                if (i >= offset)
                {
                    break;
                }

                ValueDone(frames);
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }

                // A name whose end is exactly the caret is the word being typed, not context.
                if (i >= offset)
                {
                    break;
                }

                var name = text[start..i];
                HandleName(schema, frames, name, variables,
                    ref pendingRoot, ref afterEllipsis, ref afterOn, ref afterAt, ref afterDollar,
                    ref inVariableDefinitions, ref variableAwaitingType, ref pendingDirective);
                continue;
            }

            switch (ch)
            {
                case '{':
                    afterOn = false;
                    OpenBrace(schema, frames, ref pendingRoot);
                    break;

                case '}':
                    if (frames.Count > 0)
                    {
                        frames.Pop();
                    }

                    // A closed input object is a finished value; a closed selection lands on
                    // another selection, where there is no colon to clear.
                    ValueDone(frames);
                    break;

                case '(':
                    if (pendingDirective is {} pending)
                    {
                        frames.Push(new(FrameKind.Arguments)
                        {
                            Directive = pending.Directive
                        });
                    }
                    else if (frames.TryPeek(out var enclosing) &&
                             enclosing is {Kind: FrameKind.Selection, LastField: not null})
                    {
                        var arguments = new Frame(FrameKind.Arguments)
                        {
                            LastField = enclosing.LastField
                        };
                        frames.Push(arguments);
                    }
                    else if (frames.Count == 0)
                    {
                        // Operation variable definitions: query Name(...)
                        inVariableDefinitions = true;
                    }

                    break;

                case ')':
                    if (frames.TryPeek(out var openArguments) && openArguments.Kind == FrameKind.Arguments)
                    {
                        frames.Pop();
                    }

                    inVariableDefinitions = false;
                    variableAwaitingType = false;
                    break;

                case ':':
                    if (inVariableDefinitions)
                    {
                        variableAwaitingType = true;
                    }
                    else if (frames.TryPeek(out var colonFrame))
                    {
                        colonFrame.AfterColon = true;
                    }

                    break;

                case '[':
                    frames.Push(new(FrameKind.List)
                    {
                        CurrentArgument = frames.TryPeek(out var enclosingList)
                            ? enclosingList.CurrentArgument
                            : null
                    });
                    break;

                case ']':
                    if (frames.TryPeek(out var openList) && openList.Kind == FrameKind.List)
                    {
                        frames.Pop();
                        ValueDone(frames);
                    }

                    break;
            }

            afterDollar = false;
            afterAt = false;
            pendingDirective = null;
            i++;
        }

        return Resolve(frames, variables, fragments,
            pendingRoot, afterEllipsis, afterOn, afterAt, afterDollar,
            inVariableDefinitions, variableAwaitingType);
    }

    static void HandleName(
        SchemaIndex schema,
        Stack<Frame> frames,
        string name,
        List<string> variables,
        ref string? pendingRoot,
        ref bool afterEllipsis,
        ref bool afterOn,
        ref bool afterAt,
        ref bool afterDollar,
        ref bool inVariableDefinitions,
        ref bool variableAwaitingType,
        ref PendingDirective? pendingDirective)
    {
        if (afterAt)
        {
            // The directive being applied. Nothing structural changes, but a "(" straight after it
            // opens its argument list rather than the enclosing field's.
            pendingDirective = new(schema.Directives.FirstOrDefault(_ => _.Name == name));
            afterAt = false;
            return;
        }

        pendingDirective = null;

        if (afterDollar)
        {
            if (inVariableDefinitions)
            {
                variables.Add(name);
            }
            else
            {
                // A variable used as a value closes the value the colon opened.
                ValueDone(frames);
            }

            afterDollar = false;
            return;
        }

        if (variableAwaitingType)
        {
            variableAwaitingType = false;
            return;
        }

        if (afterEllipsis)
        {
            if (name == "on")
            {
                afterOn = true;
            }

            // A named spread consumes the ellipsis either way.
            afterEllipsis = false;
            return;
        }

        if (afterOn)
        {
            // The inline fragment's (or fragment definition's) type condition.
            pendingRoot = name;
            afterOn = false;
            return;
        }

        if (frames.Count == 0)
        {
            switch (name)
            {
                case "query":
                    pendingRoot = schema.QueryTypeName ?? missingRoot;
                    break;
                case "mutation":
                    pendingRoot = schema.MutationTypeName ?? missingRoot;
                    break;
                case "subscription":
                    pendingRoot = schema.SubscriptionTypeName ?? missingRoot;
                    break;
                case "on":
                    afterOn = true;
                    break;
                case "fragment":
                    // The fragment's name follows, then "on Type" — handled by the cases above.
                    break;
            }

            return;
        }

        var frame = frames.Peek();
        switch (frame.Kind)
        {
            case FrameKind.Selection:
                if (name == "on")
                {
                    afterOn = true;
                    return;
                }

                frame.LastField = frame.Type?.Fields?.FirstOrDefault(_ => _.Name == name);
                break;

            case FrameKind.Arguments:
                if (frame.AfterColon)
                {
                    // An enum or boolean literal as the value; the argument stays current until a
                    // comma-separated next name, which arrives with AfterColon already reset.
                    frame.AfterColon = false;
                }
                else
                {
                    frame.CurrentArgument = frame.Declared.FirstOrDefault(_ => _.Name == name);
                }

                break;

            case FrameKind.InputObject:
                if (frame.AfterColon)
                {
                    frame.AfterColon = false;
                }
                else
                {
                    frame.CurrentInputField = frame.Type?.InputFields?.FirstOrDefault(_ => _.Name == name);
                }

                break;
        }
    }

    /// <summary>
    /// A finished value — a string, a number, a list, an input object or a variable reference —
    /// closes the value the colon opened, so the enclosing frame is back at a name position. A bare
    /// name (an enum value, a boolean, null) clears the flag where it is consumed instead.
    /// </summary>
    static void ValueDone(Stack<Frame> frames)
    {
        if (frames.TryPeek(out var frame) &&
            frame.Kind is FrameKind.Arguments or FrameKind.InputObject)
        {
            frame.AfterColon = false;
        }
    }

    static void OpenBrace(SchemaIndex schema, Stack<Frame> frames, ref string? pendingRoot)
    {
        if (frames.Count == 0)
        {
            frames.Push(new(FrameKind.Selection)
            {
                Type = schema.Find(pendingRoot ?? schema.QueryTypeName)
            });
            pendingRoot = null;
            return;
        }

        var current = frames.Peek();
        switch (current.Kind)
        {
            case FrameKind.Selection when pendingRoot is not null:
                // An inline fragment's selection: ... on Type {
                frames.Push(new(FrameKind.Selection) {Type = schema.Find(pendingRoot)});
                pendingRoot = null;
                break;

            case FrameKind.Selection:
                frames.Push(new(FrameKind.Selection)
                {
                    Type = schema.Find(current.LastField?.Type.Unwrap().Name)
                });
                break;

            case FrameKind.Arguments:
            case FrameKind.List:
                frames.Push(new(FrameKind.InputObject)
                {
                    Type = schema.Find(current.CurrentArgument?.Type.Unwrap().Name),
                    CurrentArgument = current.CurrentArgument
                });
                break;

            case FrameKind.InputObject:
                frames.Push(new(FrameKind.InputObject)
                {
                    Type = schema.Find(current.CurrentInputField?.Type.Unwrap().Name),
                    CurrentInputField = current.CurrentInputField
                });
                break;
        }
    }

    static ScanResult Resolve(
        Stack<Frame> frames,
        List<string> variables,
        List<string> fragments,
        string? pendingRoot,
        bool afterEllipsis,
        bool afterOn,
        bool afterAt,
        bool afterDollar,
        bool inVariableDefinitions,
        bool variableAwaitingType)
    {
        _ = pendingRoot;
        if (afterOn)
        {
            return Result(ScanMode.TypeCondition);
        }

        if (afterAt)
        {
            return Result(ScanMode.Directive);
        }

        if (afterEllipsis)
        {
            return Result(ScanMode.FragmentSpread);
        }

        if (inVariableDefinitions)
        {
            return Result(variableAwaitingType ? ScanMode.VariableType : ScanMode.None);
        }

        if (frames.Count == 0)
        {
            return Result(ScanMode.Document);
        }

        var frame = frames.Peek();
        return frame.Kind switch
        {
            FrameKind.Selection => Result(ScanMode.Selection, frame),
            FrameKind.Arguments when afterDollar => Result(ScanMode.Variable, frame),
            FrameKind.Arguments when frame.AfterColon => Result(ScanMode.ArgumentValue, frame),
            FrameKind.Arguments => Result(ScanMode.ArgumentName, frame),
            FrameKind.InputObject when afterDollar => Result(ScanMode.Variable, frame),
            FrameKind.InputObject when frame.AfterColon => Result(ScanMode.ArgumentValue, InputFieldAsArgument(frame)),
            FrameKind.InputObject => Result(ScanMode.InputField, frame),
            FrameKind.List when afterDollar => Result(ScanMode.Variable, frame),
            FrameKind.List => Result(ScanMode.ArgumentValue, frame),
            _ => Result(ScanMode.None)
        };

        ScanResult Result(ScanMode mode, Frame? current = null) =>
            new(
                mode,
                current?.Type,
                current?.LastField,
                current?.CurrentArgument,
                current?.Kind == FrameKind.InputObject ? current.Type : null,
                variables,
                fragments,
                current?.Directive);

        static Frame InputFieldAsArgument(Frame inputFrame) =>
            // Value completion inside an input object keys off the current input field's type; the
            // resolver reads CurrentArgument, so surface the field through that slot.
            new(FrameKind.InputObject)
            {
                Type = inputFrame.Type,
                CurrentArgument = inputFrame.CurrentInputField
            };
    }

    static void CollectFragments(string text, List<string> fragments)
    {
        var index = 0;
        while ((index = text.IndexOf("fragment", index, StringComparison.Ordinal)) >= 0)
        {
            index += "fragment".Length;
            while (index < text.Length &&
                   char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            var start = index;
            while (index < text.Length &&
                   (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            if (index > start)
            {
                fragments.Add(text[start..index]);
            }
        }
    }

    static int SkipString(string text, int index, int limit)
    {
        // Block string?
        if (index + 2 < text.Length &&
            text[index + 1] == '"' && text[index + 2] == '"')
        {
            var end = text.IndexOf("\"\"\"", index + 3, StringComparison.Ordinal);
            if (end < 0)
            {
                return limit;
            }

            return Math.Min(end + 3, limit);
        }

        var i = index + 1;
        while (i < text.Length && text[i] != '"' && text[i] != '\n')
        {
            if (text[i] == '\\')
            {
                i++;
            }

            i++;
        }

        return Math.Min(i + 1, limit);
    }
}
