namespace BlazorQL;

/// <summary>One diagnostic against the operation text, in one-based line/column coordinates.</summary>
public sealed record OperationDiagnostic(string Message, bool IsError, int Line, int Column);