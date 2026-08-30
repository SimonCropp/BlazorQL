namespace BlazorQL;

/// <summary>One executed operation as the history remembers it.</summary>
public sealed record HistoryItem
{
    public string Query { get; init; } = "";
    public string? Variables { get; init; }
    public string? Headers { get; init; }
    public string? OperationName { get; init; }
    public string? Label { get; set; }
    public bool Favorite { get; set; }
}