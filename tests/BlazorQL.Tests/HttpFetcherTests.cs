using System.Net.Http.Headers;

/// <summary>
/// The HTTP transport over a scripted handler: a plain JSON body yields one document, a
/// multipart/mixed incremental-delivery body yields each part in order, the request carries the
/// negotiated accept header plus the user's own, and only a non-JSON body is a failure.
/// </summary>
[TestFixture]
public class HttpFetcherTests
{
    const string url = "http://example.test/graphql";

    [Test]
    public async Task PlainJsonYieldsOneElement()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":{"id":"abc123"}}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ id }"));

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("id").GetString(), Is.EqualTo("abc123"));
    }

    [Test]
    public async Task MultipartYieldsPartsInOrder()
    {
        var body =
            "---\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            """{"data":{"deferrable":{"normalString":"Nice"}},"hasNext":true}""" + "\r\n" +
            "---\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            """{"incremental":[{"data":{"deferredString":"later"},"path":["deferrable"]}],"hasNext":true}""" + "\r\n" +
            "---\r\n" +
            "Content-Type: application/json\r\n" +
            "\r\n" +
            """{"hasNext":false}""" + "\r\n" +
            "-----\r\n";
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=\"-\"; deferSpec=20220824");
            return response;
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ deferrable { normalString ... @defer { deferredString } } }"));

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].GetProperty("data").GetProperty("deferrable").GetProperty("normalString").GetString(), Is.EqualTo("Nice"));
        Assert.That(results[1].GetProperty("incremental")[0].GetProperty("data").GetProperty("deferredString").GetString(), Is.EqualTo("later"));
        Assert.That(results[2].GetProperty("hasNext").GetBoolean(), Is.False);
    }

    [Test]
    public async Task SendsAcceptAndCustomHeadersAndCamelCaseBody()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":null}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        await Collect(
            fetcher,
            new("{ id }", OperationName: "Op"),
            new()
            {
                ["authorization"] = "Bearer token",
                ["x-custom"] = "value"
            });

        var request = handler.Request!;
        Assert.That(request.Headers.NonValidated["Accept"].ToString(), Is.EqualTo("application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8"));
        Assert.That(request.Headers.GetValues("authorization").Single(), Is.EqualTo("Bearer token"));
        Assert.That(request.Headers.GetValues("x-custom").Single(), Is.EqualTo("value"));
        Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        // Null variables are omitted, names are camelCase.
        Assert.That(handler.RequestBody, Is.EqualTo("""{"query":"{ id }","operationName":"Op"}"""));
    }

    [Test]
    public async Task CapturesStatusAndYieldsErrorsOnNonSuccessJson()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.BadRequest, """{"errors":[{"message":"boom"}]}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        Assert.That(fetcher.LastStatus, Is.Null);
        var results = await Collect(fetcher, new("{ id }"));

        // GraphQL errors ride non-200s as ordinary documents.
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("errors")[0].GetProperty("message").GetString(), Is.EqualTo("boom"));
        Assert.That(fetcher.LastStatus, Is.EqualTo(new HttpFetchStatus(400, "Bad Request")));
    }

    [Test]
    public void NonJsonBodyThrows()
    {
        var longTail = new string('x', 600);
        var handler = new FakeHandler(_ => new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent($"<html>gateway fell over {longTail}</html>", Encoding.UTF8, "text/html")
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Collect(fetcher, new("{ id }")));

        Assert.That(exception!.Message, Does.Contain("502").And.Contain("<html>gateway fell over"));
        // Only the first 500 characters of the body are reported.
        Assert.That(exception.Message, Does.Not.Contain("</html>"));
        Assert.That(fetcher.LastStatus!.StatusCode, Is.EqualTo(502));
    }

    static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    static async Task<List<JsonElement>> Collect(
        HttpFetcher fetcher,
        GraphQLRequest request,
        Dictionary<string, string>? headers = null)
    {
        List<JsonElement> results = [];
        await foreach (var element in fetcher.FetchAsync(request, headers ?? [], Cancel.None))
        {
            results.Add(element);
        }

        return results;
    }

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) :
        HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
        {
            Request = request;
            RequestBody = await request.Content!.ReadAsStringAsync(cancel);
            return respond(request);
        }
    }
}
