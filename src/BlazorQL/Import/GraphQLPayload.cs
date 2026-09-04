/// <summary>
/// The GraphQL-shaped parts of a captured request, still as they were sent: a one-line query, raw
/// variables JSON. Formatting happens once, in the importer, so every input format arrives at the
/// editors looking the same.
/// </summary>
sealed record GraphQLPayload(string Query, string? Variables, string? OperationName);
