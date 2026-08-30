namespace BlazorQL;

/// <summary>One operation of a parsed document — what run-at-caret and the picker read.</summary>
public sealed record OperationFact(string? Name, string Kind, int Start, int End);