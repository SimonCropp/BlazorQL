using Microsoft.AspNetCore.Components;

/// <summary>bUnit coverage for the per-error actions under the response pane.</summary>
[TestFixture]
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
    public void RendersARowPerActionableError()
    {
        using var context = new BunitContext();

        var cut = Render(
            context,
            [Error("first", "a"), Error("second", "b", "c")]);

        var rows = cut.FindAll("[data-testid='response-error']");
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].TextContent, Does.Contain("a").And.Contain("first"));
        Assert.That(rows[1].TextContent, Does.Contain("b.c").And.Contain("second"));
    }

    /// <summary>
    /// A validation failure, or an error from a server that strips the path, names nothing to
    /// remove. Offering a button for it would promise an edit that cannot be made.
    /// </summary>
    [Test]
    public void SkipsAnErrorWithNoPath()
    {
        using var context = new BunitContext();

        var cut = Render(
            context,
            [Error("no path here"), Error("actionable", "a")]);

        var rows = cut.FindAll("[data-testid='response-error']");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].TextContent, Does.Contain("actionable"));
    }

    /// <summary>Nothing to act on renders nothing at all, rather than an empty strip.</summary>
    [Test]
    public void RendersNothingWhenNoErrorNamesAField()
    {
        using var context = new BunitContext();

        var cut = Render(context, [Error("no path here")]);

        Assert.That(cut.FindAll("[data-testid='response-errors']"), Is.Empty);
    }

    [Test]
    public void RendersNothingWithoutErrors()
    {
        using var context = new BunitContext();

        var cut = Render(context, []);

        Assert.That(cut.FindAll("[data-testid='response-errors']"), Is.Empty);
    }

    [Test]
    public void RemoveRaisesTheErrorItBelongsTo()
    {
        using var context = new BunitContext();
        ResponseError? raised = null;

        var cut = Render(
            context,
            [Error("first", "a"), Error("second", "b")],
            EventCallback.Factory.Create<ResponseError>(this, _ => raised = _));
        cut.FindAll("[data-testid='response-error-remove']")[1]
            .Click();

        Assert.That(raised, Is.Not.Null);
        Assert.That(raised!.PathText, Is.EqualTo("b"));
    }

    /// <summary>The path is what the button promises to act on, so it names it.</summary>
    [Test]
    public void TheButtonNamesTheFieldItWouldRemove()
    {
        using var context = new BunitContext();

        var cut = Render(context, [Error("boom", "accessGroups", "members")]);

        Assert.That(
            cut.Find("[data-testid='response-error-remove']").GetAttribute("title"),
            Is.EqualTo("Remove accessGroups.members from the operation"));
    }
}
