namespace BlazorQL;

/// <summary>
/// The BlazorQL IDE. Renders the editor shell and drives the vendored Monaco/monaco-graphql stack
/// through the <c>blazorql.js</c> host module. One instance per page.
/// </summary>
public partial class BlazorQLIde :
    IAsyncDisposable
{
    internal const string OperationElementId = "blazorql-operation-editor";

    /// <summary>Seed for the operation editor. Null renders the welcome text.</summary>
    [Parameter]
    public string? DefaultQuery { get; set; }

    bool ready;
    JsModule? module;
    BlazorQLCallbacks callbacks = new();
    DotNetObjectReference<BlazorQLCallbacks>? reference;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        module = new(JS);
        reference = DotNetObjectReference.Create(callbacks);
        await module.Invoke<JsonElement>("init", reference, "blazorql");
        await module.Invoke(
            "createEditor",
            OperationElementId,
            "operation.graphql",
            "graphql",
            DefaultQuery ?? WelcomeQuery,
            null);

        ready = true;
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        reference?.Dispose();
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    // Adapted from GraphiQL's welcome comment, with BlazorQL's shortcut spellings.
    internal const string WelcomeQuery =
        """
        # Welcome to BlazorQL
        #
        # BlazorQL is an in-browser tool for writing, validating, and testing
        # GraphQL queries.
        #
        # Type queries into this side of the screen, and you will see intelligent
        # typeaheads aware of the current GraphQL type schema and live syntax and
        # validation errors highlighted within the text.
        #
        # GraphQL queries typically start with a "{" character. Lines that start
        # with a # are ignored.
        #
        # An example GraphQL query might look like:
        #
        #     {
        #       field(arg: "value") {
        #         subField
        #       }
        #     }
        #
        # Keyboard shortcuts:
        #
        #   Prettify query:  Shift-Ctrl-P (or press the prettify button)
        #
        #  Merge fragments:  Shift-Ctrl-M (or press the merge button)
        #
        #        Run Query:  Ctrl-Enter (or press the play button)
        #
        #    Auto Complete:  Space (or just start typing)
        #

        """;
}
