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

    public static DocumentInfo Parse(string text)
    {
        try
        {
            return new(text, Parser.Parse(text), null, 0, 0);
        }
        catch (GraphQLSyntaxErrorException exception)
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
