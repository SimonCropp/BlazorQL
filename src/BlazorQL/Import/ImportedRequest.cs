namespace BlazorQL;

/// <summary>
/// One GraphQL request recovered from a pasted network capture. <see cref="Query"/> and
/// <see cref="Variables"/> arrive already formatted, so the IDE only has to assign them to a tab.
/// The endpoint the request was captured from is deliberately not carried: the IDE has no endpoint
/// of its own, only a fetcher.
/// </summary>
/// <param name="Query">The operation document, prettified.</param>
/// <param name="Variables">Pretty-printed JSON, or empty when the capture had no variables.</param>
/// <param name="OperationName">
/// Set only when the document declares more than one operation, which is the case the tab's
/// operation name exists to disambiguate. A single-operation document names its own tab.
/// </param>
/// <param name="Headers">The importable headers as a JSON object, or empty when none survived.</param>
/// <param name="HeadersFound">How many headers the capture carried, before filtering.</param>
/// <param name="HeadersImported">How many of them were worth replaying.</param>
public sealed record ImportedRequest(
    string Query,
    string Variables,
    string? OperationName,
    string Headers,
    int HeadersFound,
    int HeadersImported);
