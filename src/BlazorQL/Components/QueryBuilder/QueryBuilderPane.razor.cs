namespace BlazorQL;

/// <summary>
/// The query builder: the schema as a tree of check boxes over the operation editor's text. A box
/// is checked because the text selects that field, and clicking it splices the field in or out —
/// the text stays the one thing that is true, so the tree and the editor cannot disagree for longer
/// than the editor's change debounce, and an edit made by hand shows up in the tree.
/// </summary>
/// <remarks>
/// Every change is raised as an edit (see <see cref="QueryBuilder"/>) for the IDE to apply to what
/// the editor holds at that moment, rather than as the edited text: the tree was drawn from the text
/// as the tab last recorded it, which can be a keystroke or two behind the editor.
/// </remarks>
public partial class QueryBuilderPane
{
    const string typename = "__typename";

    /// <summary>The parsed schema. Null renders the no-schema placeholder.</summary>
    [Parameter]
    public SchemaIndex? Schema { get; set; }

    /// <summary>The operation editor's text, as the active tab last recorded it.</summary>
    [Parameter]
    public string Query { get; set; } = "";

    /// <summary>Raised with an edit for the IDE to apply to the operation editor.</summary>
    [Parameter]
    public EventCallback<Func<string, string?>> OnEdit { get; set; }

    /// <summary>
    /// Raised with an edit for the IDE to apply to the variables editor, when an operation edit
    /// declared a variable or dropped one.
    /// </summary>
    [Parameter]
    public EventCallback<Func<string, string?>> OnVariablesEdit { get; set; }

    // Parsed when the text changes rather than on every render: the IDE renders on every pane-drag
    // frame and every keystroke debounce, and the tree is drawn from the same parse each time.
    string? parsedText;

    // An operation the user has started from the add buttons and not yet chosen a field for. It
    // exists only here until then: an operation with nothing selected does not parse.
    OperationType? pending;

    // The value inputs whose last entry was not a literal of their type, by input path.
    readonly HashSet<string> invalid = [];

    DocumentInfo Parsed
    {
        get
        {
            if (!ReferenceEquals(parsedText, Query) ||
                field is null)
            {
                parsedText = Query;
                field = DocumentInfo.Parse(Query);
            }

            return field;
        }
    }

    GraphQLDocument? Document =>
        Parsed.Document;

    string? SyntaxError =>
        Parsed.SyntaxError;

    bool HasOperations =>
        Document?.Definitions.OfType<GraphQLOperationDefinition>().Any() == true;

    /// <summary>
    /// The operation the pending section offers fields for: the one started from an add button, or a
    /// query when the document has none — a blank tab, or the welcome text — so the tree is there to
    /// click from the start.
    /// </summary>
    OperationType? PendingKind
    {
        get
        {
            if (pending is not null)
            {
                return pending;
            }

            if (HasOperations)
            {
                return null;
            }

            return OperationType.Query;
        }
    }

    static readonly OperationType[] kinds =
    [
        OperationType.Query,
        OperationType.Mutation,
        OperationType.Subscription
    ];

    /// <summary>The operation kinds the schema has a root type for.</summary>
    IEnumerable<OperationType> Kinds =>
        kinds.Where(_ => QueryBuilder.RootType(Schema!, _) is not null);

    static string Keyword(OperationType kind) =>
        kind switch
        {
            OperationType.Mutation => "mutation",
            OperationType.Subscription => "subscription",
            _ => "query"
        };

    // A string rather than a bool: a bool would render the attribute bare, or not at all.
    static string Checked(bool value) =>
        value ? "true" : "false";

    bool IsComposite(TypeRef type) =>
        QueryBuilder.IsComposite(Schema?.Find(type.Unwrap().Name));

    IntrospectionType? InputObject(TypeRef type) =>
        Schema?.Find(type.Unwrap().Name) is {Kind: "INPUT_OBJECT"} input ? input : null;

    /// <summary>
    /// The fields a type offers. A deprecated one is left out unless the text already selects it,
    /// where it stays visible so it can be unchecked.
    /// </summary>
    static IEnumerable<IntrospectionField> VisibleFields(IntrospectionType type, GraphQLSelectionSet? set) =>
        (type.Fields ?? []).Where(_ => !_.IsDeprecated || QueryBuilder.Field(set, _.Name) is not null);

    static IEnumerable<IntrospectionInputValue> VisibleArguments(IntrospectionField field, GraphQLField selected) =>
        field.Args.Where(_ => !_.IsDeprecated || QueryBuilder.Argument(selected, _.Name) is not null);

    static IEnumerable<IntrospectionInputValue> VisibleInputFields(IntrospectionType type, GraphQLObjectValue value) =>
        (type.InputFields ?? []).Where(_ => !_.IsDeprecated || QueryBuilder.InputField(value, _.Name) is not null);

    /// <summary>
    /// The document's fragments that can be spread where the type is selected — those on that very
    /// type — leaving out the fragment being edited, which cannot spread itself.
    /// </summary>
    IEnumerable<string> SpreadableFragments(IntrospectionType type, string? self) =>
        (Document?.Definitions ?? [])
        .OfType<GraphQLFragmentDefinition>()
        .Where(_ => _.TypeCondition.Type.Name.StringValue == type.Name)
        .Select(_ => _.FragmentName.Name.StringValue)
        .Where(_ => _ != self)
        .Distinct(StringComparer.Ordinal);

    static string Title(IntrospectionField field) =>
        Title(field.Name, field.Type, field.Description);

    static string Title(IntrospectionInputValue value) =>
        Title(value.Name, value.Type, value.Description);

    static string Title(string name, TypeRef type, string? description)
    {
        var signature = $"{name}: {type.Display()}";
        if (string.IsNullOrWhiteSpace(description))
        {
            return signature;
        }

        return $"{signature}\n\n{description}";
    }

    static string? InputMode(string scalar) =>
        scalar switch
        {
            "Int" => "numeric",
            "Float" => "decimal",
            _ => null
        };

    static string Key(int definition, BuilderStep[] path, string[] input) =>
        $"{definition}/{string.Join('/', path.Select(_ => _.IsFragment ? "..." + _.Name : _.Name))}({string.Join('.', input)})";

    /// <summary>
    /// Raises an operation edit, then the variables edit that keeps the variables document in step
    /// with whatever it declared or dropped — switching an argument to a variable, or taking out the
    /// last field that read one.
    /// </summary>
    async Task Edit(Func<string, string?> edit, int definition)
    {
        Func<string, string?>? variablesEdit = null;
        await OnEdit.InvokeAsync(text =>
        {
            var edited = edit(text);
            if (edited is not null)
            {
                variablesEdit = QueryBuilder.VariablesEdit(Schema!, text, edited, definition);
            }

            return edited;
        });

        if (variablesEdit is not null)
        {
            await OnVariablesEdit.InvokeAsync(variablesEdit);
        }
    }

    Task Edit(Func<string, string?> edit) =>
        OnEdit.InvokeAsync(edit);

    Task Toggle(int definition, BuilderStep[] path) =>
        Edit(_ => QueryBuilder.ToggleSelection(Schema!, _, definition, path), definition);

    Task ToggleSpread(int definition, BuilderStep[] path, string fragment) =>
        Edit(_ => QueryBuilder.ToggleSpread(Schema!, _, definition, path, fragment), definition);

    Task ToggleArgument(int definition, BuilderStep[] path, string[] input) =>
        Edit(_ => QueryBuilder.ToggleArgument(Schema!, _, definition, path, input), definition);

    Task ToggleVariable(int definition, BuilderStep[] path, string argument) =>
        Edit(_ => QueryBuilder.ToggleVariable(Schema!, _, definition, path, argument), definition);

    /// <summary>
    /// Writes what a value input holds, when it is a literal of the input's type. When it is not —
    /// letters in a number — the text is left alone and the input marked, rather than writing a
    /// literal the server would refuse or a document that would not parse.
    /// </summary>
    Task SetValue(int definition, BuilderStep[] path, string[] input, TypeRef type, object? entered)
    {
        var key = Key(definition, path, input);
        if (QueryBuilder.Literal(Schema!, type, entered?.ToString() ?? "") is not { } literal)
        {
            invalid.Add(key);
            return Task.CompletedTask;
        }

        invalid.Remove(key);
        return Edit(_ => QueryBuilder.SetArgument(Schema!, _, definition, path, input, literal), definition);
    }

    Task Rename(int definition, ChangeEventArgs args) =>
        Edit(_ => QueryBuilder.RenameOperation(_, definition, args.Value?.ToString() ?? ""));

    Task RemoveOperation(int definition) =>
        Edit(_ => QueryBuilder.RemoveOperation(_, definition));

    void Begin(OperationType kind) =>
        pending = kind;

    void Cancel() =>
        pending = null;

    /// <summary>Writes the started operation, with the first field chosen for it.</summary>
    Task Start(OperationType kind, string field)
    {
        pending = null;
        return Edit(_ => QueryBuilder.AddOperation(Schema!, _, kind, field));
    }
}
