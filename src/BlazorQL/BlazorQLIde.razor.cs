namespace BlazorQL;

/// <summary>
/// The BlazorQL IDE. Renders the editor shell and drives the vendored Monaco/monaco-graphql stack
/// through the <c>blazorql.js</c> host module. One instance per page.
/// </summary>
public partial class BlazorQLIde :
    IAsyncDisposable
{
    internal const string OperationElementId = "blazorql-operation-editor";
    internal const string ResponseElementId = "blazorql-response-editor";
    const string OperationUri = "operation.graphql";
    const string ResponseUri = "response.json";

    /// <summary>Transports requests — including the introspection the schema is built from.</summary>
    [Parameter]
    [EditorRequired]
    public IGraphQLFetcher Fetcher { get; set; } = null!;

    /// <summary>Seed for the operation editor. Null renders the welcome text.</summary>
    [Parameter]
    public string? DefaultQuery { get; set; }

    /// <summary>Fires after the schema is introspected and pushed to the editors.</summary>
    [Parameter]
    public EventCallback OnSchemaLoaded { get; set; }

    bool ready;
    bool running;
    JsModule? module;
    readonly BlazorQLCallbacks callbacks = new();
    DotNetObjectReference<BlazorQLCallbacks>? reference;
    CancellationTokenSource? execution;

    /// <summary>The schema printed as SDL, once loaded.</summary>
    public string? SchemaSdl { get; private set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        module = new(JS);
        reference = DotNetObjectReference.Create(callbacks);
        callbacks.EditorAction += OnEditorAction;
        if (Fetcher is LocalSchemaFetcher local)
        {
            local.Attach(module, callbacks);
        }

        await module.Invoke<JsonElement>("init", reference, "blazorql");
        await module.Invoke(
            "createEditor",
            OperationElementId,
            OperationUri,
            "graphql",
            DefaultQuery ?? WelcomeQuery,
            null);
        await module.Invoke(
            "createEditor",
            ResponseElementId,
            ResponseUri,
            "json",
            "",
            """{"readOnly": true, "lineNumbers": "off", "wordWrap": "on", "contextmenu": false}""");

        // Monaco KeyMod.CtrlCmd | KeyCode.Enter.
        await module.Invoke("addAction", OperationUri, "blazorql-run", "Run Operation", "[2051]");

        await LoadSchema();

        ready = true;
        StateHasChanged();
    }

    async Task LoadSchema()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            JsonElement? introspection = null;
            await foreach (var payload in Fetcher.FetchAsync(new(IntrospectionQuery), Headers(), cts.Token))
            {
                introspection = payload;
                break;
            }

            if (introspection is null)
            {
                await SetResponse("""{"errors":[{"message":"Introspection returned no result."}]}""");
                return;
            }

            SchemaSdl = await module!.Invoke<string>("setSchemaFromIntrospection", introspection.Value.GetRawText());
            await OnSchemaLoaded.InvokeAsync();
        }
        catch (Exception exception)
        {
            await SetResponse(JsonSerializer.Serialize(new
            {
                errors = new[] {new {message = $"Introspection failed: {exception.Message}"}}
            }));
        }
    }

    void OnEditorAction(string actionId)
    {
        if (actionId == "blazorql-run")
        {
            _ = InvokeAsync(RunOrStop);
        }
    }

    async Task RunOrStop()
    {
        if (running)
        {
            execution?.Cancel();
            return;
        }

        await Run();
    }

    async Task Run()
    {
        var query = await module!.Invoke<string>("getValue", OperationUri);
        execution = new();
        running = true;
        StateHasChanged();

        var merger = new IncrementalMerger();
        try
        {
            await foreach (var payload in Fetcher.FetchAsync(new(query), Headers(), execution.Token))
            {
                merger.Add(payload);
                await SetResponse(merger.Render());
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; whatever arrived stays on screen.
        }
        catch (Exception exception)
        {
            await SetResponse(JsonSerializer.Serialize(new
            {
                errors = new[] {new {message = exception.Message}}
            }));
        }
        finally
        {
            execution.Dispose();
            execution = null;
            running = false;
            StateHasChanged();
        }
    }

    ValueTask SetResponse(string text) =>
        module!.Invoke("setValue", ResponseUri, text);

    static Dictionary<string, string> Headers() => [];

    public async ValueTask DisposeAsync()
    {
        callbacks.EditorAction -= OnEditorAction;
        execution?.Cancel();
        reference?.Dispose();
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    // The standard introspection query, as graphql-js emits it (descriptions and deprecated
    // members included; nine levels of type nesting).
    internal const string IntrospectionQuery =
        """
        query IntrospectionQuery {
          __schema {
            description
            queryType { name kind }
            mutationType { name kind }
            subscriptionType { name kind }
            types { ...FullType }
            directives {
              name
              description
              isRepeatable
              locations
              args(includeDeprecated: true) { ...InputValue }
            }
          }
        }

        fragment FullType on __Type {
          kind
          name
          description
          specifiedByURL
          fields(includeDeprecated: true) {
            name
            description
            args(includeDeprecated: true) { ...InputValue }
            type { ...TypeRef }
            isDeprecated
            deprecationReason
          }
          inputFields(includeDeprecated: true) { ...InputValue }
          interfaces { ...TypeRef }
          enumValues(includeDeprecated: true) {
            name
            description
            isDeprecated
            deprecationReason
          }
          possibleTypes { ...TypeRef }
        }

        fragment InputValue on __InputValue {
          name
          description
          type { ...TypeRef }
          defaultValue
          isDeprecated
          deprecationReason
        }

        fragment TypeRef on __Type {
          kind
          name
          ofType {
            kind
            name
            ofType {
              kind
              name
              ofType {
                kind
                name
                ofType {
                  kind
                  name
                  ofType {
                    kind
                    name
                    ofType {
                      kind
                      name
                      ofType {
                        kind
                        name
                        ofType {
                          kind
                          name
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

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
