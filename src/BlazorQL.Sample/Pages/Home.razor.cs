namespace BlazorQL.Sample;

/// <summary>
/// The sample's default page: an ordinary Blazor page consuming the GraphQL API — a query on
/// load, a mutation behind a button, and a subscription streamed into a list — all through the
/// shared fetcher, so the debug sidecar shows each request as it happens.
/// </summary>
public partial class Home :
    IDisposable
{
    // A header the sidecar can show; a real app would carry auth here.
    static readonly Dictionary<string, string> headers = new()
    {
        ["x-sample-app"] = "home"
    };

    Profile? profile;
    string? profileError;
    string echoInput = "Hello from Blazor";
    string? echoResult;
    readonly List<string> greetings = [];
    CancelSource? feed;

    sealed record Profile(string Name, int Age, List<string> Friends);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            // begin-snippet: homeQuery
            // An ordinary app query through the shared fetcher — the sidecar records it like
            // any other, alongside everything the query explorer sends.
            var document = await QueryAsync(
                """
                query Profile {
                  person {
                    name
                    age(delay: 21)
                    friends {
                      name
                    }
                  }
                }
                """);
            // end-snippet
            var person = document.GetProperty("data").GetProperty("person");
            profile = new(
                person.GetProperty("name").GetString()!,
                person.GetProperty("age").GetInt32(),
                [.. person.GetProperty("friends").EnumerateArray().Select(_ => _.GetProperty("name").GetString()!)]);
        }
        catch (Exception exception)
        {
            profileError = exception.Message;
        }
    }

    async Task SendAsync()
    {
        var document = await QueryAsync(
            """
            mutation Echo($value: String) {
              setString(value: $value)
            }
            """,
            JsonSerializer.SerializeToElement(new {value = echoInput}));
        echoResult = document.GetProperty("data").GetProperty("setString").GetString();
    }

    async Task ToggleFeedAsync()
    {
        if (feed is not null)
        {
            await feed.CancelAsync();
            return;
        }

        greetings.Clear();
        using var cancellation = new CancelSource();
        feed = cancellation;
        try
        {
            var request = new GraphQLRequest("subscription Greetings { message(delay: 400) }");
            await foreach (var document in Fetcher.FetchAsync(request, headers, cancellation.Token))
            {
                greetings.Add(document.GetProperty("data").GetProperty("message").GetString()!);
                StateHasChanged();
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the button or by leaving the page.
        }
        finally
        {
            feed = null;
        }
    }

    // The fetcher shape is a stream of documents; a query or mutation is the one-document case.
    async Task<JsonElement> QueryAsync(string query, JsonElement? variables = null)
    {
        await foreach (var document in Fetcher.FetchAsync(new(query, variables), headers, Cancel.None))
        {
            return document;
        }

        throw new InvalidOperationException("The request produced no response document.");
    }

    public void Dispose() =>
        feed?.Cancel();
}
