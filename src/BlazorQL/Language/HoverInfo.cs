namespace BlazorQL;

/// <summary>What hover shows for the token at an offset, with the token span for highlighting.</summary>
public sealed record HoverInfo(string Markdown, int Start, int End);