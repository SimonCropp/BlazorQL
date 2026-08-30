namespace BlazorQL;

/// <summary>One GraphQL request as a fetcher sends it.</summary>
public sealed record GraphQLRequest(
    string Query,
    JsonElement? Variables = null,
    string? OperationName = null);
