using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

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
        var document = Markdown.Parse(content, pipeline);
        if (preview &&
            document.Count == 0)
        {
            return "";
        }

        Sanitize(document);

        var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(preview ? document[0] : document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// Takes away every target a browser would run rather than fetch. Disabling raw HTML keeps a
    /// description from writing its own tags; it says nothing about where the tags markdown itself
    /// produces may point. See <see cref="UrlSafety"/>.
    /// </summary>
    /// <remarks>
    /// Markdown makes an <c>href</c> from two node types, and both are checked. A link or image
    /// keeps its text and loses its target. An angle-bracket autolink, <c>&lt;scheme:anything&gt;</c>,
    /// takes any scheme at all and is not a <see cref="LinkInline"/>; its text is its target, so
    /// emptying the target would empty the text too. It is replaced by that text instead.
    /// </remarks>
    static void Sanitize(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!UrlSafety.IsRenderable(link.Url))
            {
                link.Url = "";
            }
        }

        // Collected first: replacing a node while walking the tree would cut the walk short.
        var autolinks = document
            .Descendants<AutolinkInline>()
            .Where(_ => !_.IsEmail && !UrlSafety.IsRenderable(_.Url))
            .ToList();
        foreach (var autolink in autolinks)
        {
            autolink.ReplaceBy(new LiteralInline(autolink.Url));
        }
    }
}
