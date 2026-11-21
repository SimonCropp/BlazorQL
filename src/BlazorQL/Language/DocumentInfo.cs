namespace BlazorQL;

/// <summary>
/// A parsed GraphQL document: the AST when the text parses, the syntax error when it does not, and
/// the derived facts both the execution flow and the language services read.
/// </summary>
public sealed class DocumentInfo
{
    DocumentInfo(string text, GraphQLDocument? document, string? syntaxError, int errorLine, int errorColumn)
    {
        Text = text;
        Document = document;
        SyntaxError = syntaxError;
        SyntaxErrorLine = errorLine;
        SyntaxErrorColumn = errorColumn;
    }

    public string Text { get; }
    public GraphQLDocument? Document { get; }
    public string? SyntaxError { get; }
    public int SyntaxErrorLine { get; }
    public int SyntaxErrorColumn { get; }

    public bool Parses => Document is not null;

    /// <summary>
    /// Parses <paramref name="text"/>, never throwing for what the text holds. Both of the parser's
    /// refusals come back as the error: a syntax error, and nesting past its depth limit (128 AST
    /// nodes, about 60 levels of selection), which it raises as an exception of its own to keep the
    /// recursive descent off the end of the stack. Everything that reads a document — the editor's
    /// diagnostics, execute, prettify, import, the builder, and the fetchers in front of the host
    /// app's own requests — asks here whether it parses, so the depth refusal has to be an answer
    /// rather than an escape.
    /// </summary>
    public static DocumentInfo Parse(string text)
    {
        try
        {
            return new(text, Parser.Parse(text), null, 0, 0);
        }
        catch (GraphQLParserException exception)
        {
            return new(text, null, exception.Description, exception.Location.Line, exception.Location.Column);
        }
    }

    public IReadOnlyList<OperationFact> Operations
    {
        get
        {
            if (Document is null)
            {
                return [];
            }

            return
            [
                .. Document.Definitions
                    .OfType<GraphQLOperationDefinition>()
                    .Select(_ => new OperationFact(
                        _.Name?.StringValue,
                        _.Operation.ToString().ToLowerInvariant(),
                        _.Location.Start,
                        _.Location.End))
            ];
        }
    }

    public IReadOnlyList<GraphQLFragmentDefinition> Fragments
    {
        get
        {
            if (Document is null)
            {
                return [];
            }

            return [.. Document.Definitions.OfType<GraphQLFragmentDefinition>()];
        }
    }

    /// <summary>The operation whose span contains <paramref name="offset"/>, else the first.</summary>
    public OperationFact? OperationAt(int offset)
    {
        var operations = Operations;
        if (operations.Count == 0)
        {
            return null;
        }

        return operations.FirstOrDefault(_ => offset >= _.Start && offset <= _.End) ?? operations[0];
    }

    /// <summary>The operation definition node by name, or the single/first one.</summary>
    public GraphQLOperationDefinition? OperationNode(string? name)
    {
        var operations = Document?.Definitions.OfType<GraphQLOperationDefinition>().ToList();
        if (operations is not {Count: > 0})
        {
            return null;
        }

        if (name is null)
        {
            return operations[0];
        }

        return operations.FirstOrDefault(_ => _.Name?.StringValue == name) ?? operations[0];
    }
}
