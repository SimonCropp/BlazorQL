namespace BlazorQL;

/// <summary>Everything one tab remembers while another tab is active.</summary>
public sealed record TabState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Query { get; set; } = "";
    public string Variables { get; set; } = "";
    public string Headers { get; set; } = "";
    public string? OperationName { get; set; }
    public string Response { get; set; } = "";
    public string? RenameOverride { get; set; }
}