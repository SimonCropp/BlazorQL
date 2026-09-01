using Global = BlazorMonaco.Editor.Global;

/// <summary>
/// Creation and disposal of the editors' named models. The uris are page-global — the point of
/// them, since tests and the language providers address editors by uri — so a model outlives the
/// component that made it unless that component hands the uri back, and monaco throws rather than
/// replacing a model that already exists.
/// </summary>
static class NamedModels
{
    /// <summary>
    /// Creates the model at <paramref name="uri"/>, first clearing out anything an instance whose
    /// teardown never ran left sitting there.
    /// </summary>
    public static async Task<TextModel> Create(IJSRuntime js, string value, string language, string uri)
    {
        var leaked = await Global.GetModel(js, uri);
        await Dispose(leaked);
        return await Global.CreateModel(js, value, language, uri);
    }

    /// <summary>Best-effort disposal: teardown must not take the page down with it.</summary>
    public static async Task Dispose(TextModel? model)
    {
        if (model is null)
        {
            return;
        }

        try
        {
            await model.DisposeModel();
        }
        catch (JSException)
        {
            // The model may already be gone.
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the editors with it.
        }
    }
}
