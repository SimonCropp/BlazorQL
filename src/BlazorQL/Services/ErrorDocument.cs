/// <summary>
/// A GraphQL error response synthesized locally — a failed introspection, a malformed variables
/// document, an exception from the fetcher — so the response pane renders it like any other result.
/// </summary>
sealed record ErrorDocument(IReadOnlyList<ErrorEntry> Errors)
{
    public static ErrorDocument From(string message) =>
        new([new(message)]);
}