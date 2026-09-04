namespace BlazorQL;

/// <summary>
/// Paste a request captured from a browser's network tab and turn it into tabs. The parsing is
/// <see cref="RequestImporter"/>'s; this component reports what was found and hands the requests up.
/// The endpoint the request named is discarded — the IDE keeps talking to the fetcher its host
/// configured.
/// </summary>
public partial class ImportRequestDialog
{
    const string placeholder = "curl 'https://example.com/graphql' -H 'authorization: Bearer …' --data-raw '{\"query\":\"…\"}'";

    /// <summary>
    /// False drops the header counts from the summary: with the headers editor disabled the IDE
    /// never sends tab headers, so reporting them as imported would promise something untrue.
    /// </summary>
    [Parameter]
    public bool HeadersEnabled { get; set; } = true;

    /// <summary>Raised with every parsed request — more than one when the body was a batched array.</summary>
    [Parameter]
    public EventCallback<IReadOnlyList<ImportedRequest>> OnImport { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    ElementReference input;
    string text = "";
    string? error;
    IReadOnlyList<ImportedRequest> requests = [];

    /// <summary>
    /// Parsed on every input rather than behind a debounce. The importer is pure C# over a few
    /// kilobytes, and the interaction this exists for is a single paste — one event, which a
    /// debounce would only delay, leaving the button looking broken for as long as it ran.
    /// </summary>
    void OnInput(ChangeEventArgs args)
    {
        text = args.Value as string ?? "";
        // An empty box is not a failure, it is nothing yet.
        if (string.IsNullOrWhiteSpace(text))
        {
            error = null;
            requests = [];
            return;
        }

        var (_, parsed, message) = RequestImporter.Import(text);
        error = message;
        requests = parsed;
    }

    // Enter inserts a newline: the field is multi-line and a pasted curl is full of continuations.
    // Ctrl-Enter commits, matching the IDE's execute chord. Escape is deliberately not handled here
    // — it belongs to DialogShell's overlay, and catching it would break close-on-Escape.
    Task OnKeyDown(KeyboardEventArgs args)
    {
        if (args is {Key: "Enter", CtrlKey: true} &&
            requests.Count > 0)
        {
            return Import();
        }

        return Task.CompletedTask;
    }

    Task Import() =>
        OnImport.InvokeAsync(requests);

    string Summary
    {
        get
        {
            if (error is not null)
            {
                return error;
            }

            if (requests.Count == 0)
            {
                return "";
            }

            var parts = new List<string>();
            if (requests.Count == 1)
            {
                parts.Add(Describe(requests[0]));
                var variables = VariableCount(requests[0]);
                if (variables > 0)
                {
                    parts.Add($"{variables} {Plural(variables, "variable")}");
                }
            }
            else
            {
                parts.Add($"{requests.Count} operations");
                parts.Add(string.Join(", ", requests.Take(3).Select(Describe)) +
                          (requests.Count > 3 ? $", +{requests.Count - 3} more" : ""));
            }

            // One captured request has one header set however many operations it batched, so the
            // counts are read off the first rather than summed across identical copies.
            var request = requests[0];
            if (request.HeadersFound > 0)
            {
                parts.Add(
                    HeadersEnabled
                        ? $"{request.HeadersImported} of {request.HeadersFound} {Plural(request.HeadersFound, "header")} imported"
                        : "headers ignored");
            }

            return string.Join(" · ", parts);
        }
    }

    // The operation kind is not in the payload and does not belong there — it is one parse of an
    // already-formatted document away.
    static string Describe(ImportedRequest request)
    {
        var operations = DocumentInfo.Parse(request.Query).Operations;
        if (operations.Count == 0)
        {
            return request.OperationName ?? "operation";
        }

        var operation = operations.FirstOrDefault(_ => _.Name == request.OperationName) ??
                        operations[0];
        return operation.Name is null
            ? operation.Kind
            : $"{operation.Kind} {operation.Name}";
    }

    static int VariableCount(ImportedRequest request)
    {
        if (request.Variables.Length == 0)
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(request.Variables);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().Count()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    static string Plural(int count, string word) =>
        count == 1
            ? word
            : $"{word}s";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The shell's own panel focus is off, so this one lands: the dialog exists to be
            // pasted into.
            await input.FocusAsync();
        }
    }
}
