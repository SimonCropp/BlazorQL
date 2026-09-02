using Markdig;

namespace BlazorQL;

/// <summary>
/// Renders markdown (descriptions, deprecation reasons) as HTML. Preview clamps to the first
/// block, for the one-paragraph field-row previews.
/// </summary>
public partial class MarkdownView
{
    static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        // Schema descriptions are endpoint-controlled and the result is rendered as a MarkupString.
        .DisableHtml()
        .Build();

    string? html;

    [Parameter]
    public string? Content { get; set; }

    /// <summary>True renders only the first block (the first paragraph).</summary>
    [Parameter]
    public bool Preview { get; set; }

    /// <summary>Plain text shown when there is no content.</summary>
    [Parameter]
    public string? Fallback { get; set; }

    protected override void OnParametersSet() =>
        html = string.IsNullOrWhiteSpace(Content)
            ? null
            : Render(Content, Preview);

    static string Render(string content, bool preview)
    {
        if (!preview)
        {
            return Markdown.ToHtml(content, pipeline);
        }

        var document = Markdown.Parse(content, pipeline);
        if (document.Count == 0)
        {
            return "";
        }

        var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document[0]);
        writer.Flush();
        return writer.ToString();
    }
}
