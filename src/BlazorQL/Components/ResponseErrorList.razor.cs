namespace BlazorQL;

/// <summary>
/// The strip under the response pane: one row per error that names a field, each offering to take
/// that field out of the operation.
/// </summary>
/// <remarks>
/// Per error rather than one button that strips them all. Removal is not always the right answer —
/// a field that failed for want of an argument wants the argument — so the choice stays with the
/// reader, one field at a time.
/// </remarks>
public partial class ResponseErrorList
{
    /// <summary>Every error the response carried, acted on or not.</summary>
    [Parameter]
    public IReadOnlyList<ResponseError> Errors { get; set; } = [];

    /// <summary>Raised with the error whose field the reader asked to remove.</summary>
    [Parameter]
    public EventCallback<ResponseError> OnRemove { get; set; }

    /// <summary>
    /// The errors there is something to do about. One raised before execution reached a field — a
    /// validation failure, or a server that strips the path on its way out — names nothing to
    /// remove, and a row offering to remove it would be a lie.
    /// </summary>
    IReadOnlyList<ResponseError> Actionable =>
        [.. Errors.Where(_ => _.HasPath)];
}
