using Microsoft.AspNetCore.Components;
/// <summary>bUnit coverage for the per-error actions under the response pane.</summary>
public class ResponseErrorListTests
{
    static IRenderedComponent<ResponseErrorList> Render(
        BunitContext context,
        IReadOnlyList<ResponseError> errors,
        EventCallback<ResponseError>? onRemove = null) =>
        context.Render<ResponseErrorList>(
            _ =>
            {
                _.Add(component => component.Errors, errors);
                if (onRemove is { } callback)
                {
                    _.Add(component => component.OnRemove, callback);
                }
            });

    static ResponseError Error(string message, params string[] path) =>
        new(message, path);

    [Test]
    public async Task RendersARowPerActionableError()
    {
        using var context = new BunitContext();

        var cut = Render(
            context,
            [Error("first", "a"), Error("second", "b", "c")]);

        var rows = cut.FindAll("[data-testid='response-error']");
        await Assert.That(rows).Count().IsEqualTo(2);
        await Assert.That(rows[0].TextContent).Contains("a").And.Contains("first");
        await Assert.That(rows[1].TextContent).Contains("b.c").And.Contains("second");
    }

    /// <summary>
    /// A validation failure, or an error from a server that strips the path, names nothing to
    /// remove. Offering a button for it would promise an edit that cannot be made.
    /// </summary>
    [Test]
    public async Task SkipsAnErrorWithNoPath()
    {
        using var context = new BunitContext();

        var cut = Render(
            context,
            [Error("no path here"), Error("actionable", "a")]);

        var rows = cut.FindAll("[data-testid='response-error']");
        await Assert.That(rows).Count().IsEqualTo(1);
        await Assert.That(rows[0].TextContent).Contains("actionable");
    }

    /// <summary>Nothing to act on renders nothing at all, rather than an empty strip.</summary>
    [Test]
    public async Task RendersNothingWhenNoErrorNamesAField()
    {
        using var context = new BunitContext();

        var cut = Render(context, [Error("no path here")]);

        await Assert.That(cut.FindAll("[data-testid='response-errors']")).IsEmpty();
    }

    [Test]
    public async Task RendersNothingWithoutErrors()
    {
        using var context = new BunitContext();

        var cut = Render(context, []);

        await Assert.That(cut.FindAll("[data-testid='response-errors']")).IsEmpty();
    }

    [Test]
    public async Task RemoveRaisesTheErrorItBelongsTo()
    {
        using var context = new BunitContext();
        ResponseError? raised = null;

        var cut = Render(
            context,
            [Error("first", "a"), Error("second", "b")],
            EventCallback.Factory.Create<ResponseError>(this, _ => raised = _));
        cut.FindAll("[data-testid='response-error-remove']")[1]
            .Click();

        await Assert.That(raised).IsNotNull();
        await Assert.That(raised!.PathText).IsEqualTo("b");
    }

    /// <summary>The path is what the button promises to act on, so it names it.</summary>
    [Test]
    public async Task TheButtonNamesTheFieldItWouldRemove()
    {
        using var context = new BunitContext();

        var cut = Render(context, [Error("boom", "accessGroups", "members")]);

        await Assert.That(cut.Find("[data-testid='response-error-remove']").GetAttribute("title")).IsEqualTo("Remove accessGroups.members from the operation");
    }
}