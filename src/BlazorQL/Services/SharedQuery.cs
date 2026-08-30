namespace BlazorQL;

/// <summary>
/// What a share link carries: the operation text and the variables text. Headers are excluded by
/// construction — there is nowhere in this shape to put them.
/// </summary>
public sealed record SharedQuery(string Query, string Variables);