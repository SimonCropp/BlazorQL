namespace BlazorQL;

/// <summary>The transport outcome of one HTTP fetch — what the status footer shows.</summary>
public sealed record HttpFetchStatus(int StatusCode, string? ReasonPhrase);
