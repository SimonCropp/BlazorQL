/// <summary>
/// The HTTP transport over a scripted handler: a plain JSON body yields one document, a
/// multipart/mixed incremental-delivery body yields each part in order, the request carries the
/// negotiated accept header plus the user's own, and only a non-JSON body is a failure.
/// </summary>
public class HttpFetcherTests
{
    const string url = "http://example.test/graphql";

    [Test]
    public async Task PlainJsonYieldsOneElement()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":{"id":"abc123"}}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ id }"));

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].GetProperty("data").GetProperty("id").GetString()).IsEqualTo("abc123");
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

        await Assert.That(results).Count().IsEqualTo(3);
        await Assert.That(results[0].GetProperty("data").GetProperty("deferrable").GetProperty("normalString").GetString()).IsEqualTo("Nice");
        await Assert.That(results[1].GetProperty("incremental")[0].GetProperty("data").GetProperty("deferredString").GetString()).IsEqualTo("later");
        await Assert.That(results[2].GetProperty("hasNext").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// GraphQL Yoga's framing: each part ends with "\r\n---" and nothing more, and the line break
    /// after the delimiter comes only with the next part. A part is complete once its delimiter has
    /// arrived, so it has to be handed over then rather than when the next part starts — or the
    /// initial payload of a @defer waits for the deferred one, and @defer does nothing visible.
    /// </summary>
    [Test]
    public async Task AYogaPartArrivesBeforeTheNextOneStarts()
    {
        var initial = """{"data":{"fast":"now"},"hasNext":true}""";
        var deferred = """{"incremental":[{"data":{"slow":"later"},"path":[]}],"hasNext":false}""";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var body = new GatedStream(
            gate.Task,
            "\r\n---" +
            "\r\nContent-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {initial.Length}\r\n\r\n" +
            initial + "\r\n---",
            "\r\nContent-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {deferred.Length}\r\n\r\n" +
            deferred + "\r\n---" +
            "--\r\n");
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(body)
            };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=\"-\"");
            return response;
        });
        var fetcher = new HttpFetcher(new(handler), url);

        await using var parts = fetcher.FetchAsync(new("{ fast ... @defer { slow } }"), new Dictionary<string, string>(), Cancel.None).GetAsyncEnumerator();

        // The first part, while the server is still working on the second.
        var first = await parts.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(first).IsTrue();
        await Assert.That(parts.Current.GetProperty("data").GetProperty("fast").GetString()).IsEqualTo("now");

        gate.SetResult();
        await Assert.That(await parts.MoveNextAsync()).IsTrue();
        await Assert.That(parts.Current.GetProperty("hasNext").GetBoolean()).IsFalse();
        await Assert.That(await parts.MoveNextAsync()).IsFalse();
    }

    /// <summary>
    /// The boundary quoted or not, a part without headers, transport padding after a delimiter, and
    /// preamble before the first: the edges of RFC 2046 a server is entitled to use.
    /// </summary>
    [Test]
    public async Task MultipartFramingVariantsAllRead()
    {
        var body =
            "preamble to ignore\r\n" +
            "--gc0p4Jq0M2Yt08jU534c0p  \r\n" +
            "\r\n" +
            """{"data":{"a":1},"hasNext":true}""" + "\r\n" +
            "--gc0p4Jq0M2Yt08jU534c0p\r\n" +
            "content-type: application/json\r\n" +
            "\r\n" +
            """{"hasNext":false}""" + "\r\n" +
            "--gc0p4Jq0M2Yt08jU534c0p--\r\n" +
            "epilogue to ignore";
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=gc0p4Jq0M2Yt08jU534c0p");
            return response;
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ a }"));

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].GetProperty("data").GetProperty("a").GetInt32()).IsEqualTo(1);
        await Assert.That(results[1].GetProperty("hasNext").GetBoolean()).IsFalse();
    }

    /// <summary>
    /// A part larger than the read buffer, delivered a few bytes at a time, so the delimiter search
    /// has to carry across reads and the buffer has to grow.
    /// </summary>
    [Test]
    public async Task APartSplitAcrossManySmallReadsStillReads()
    {
        var large = new string('x', 40_000);
        var text =
            "---\r\n\r\n" +
            $$"""{"data":{"text":"{{large}}"},"hasNext":true}""" + "\r\n" +
            "---\r\n\r\n" +
            """{"hasNext":false}""" + "\r\n" +
            "-----\r\n";
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new TrickleStream(Encoding.UTF8.GetBytes(text), 7))
            };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=\"-\"");
            return response;
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ text }"));

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].GetProperty("data").GetProperty("text").GetString()).IsEqualTo(large);
    }

    /// <summary>Hands out its first chunk at once and the rest only once the gate opens, the way a server still resolving does.</summary>
    sealed class GatedStream(Task gate, string first, string rest) :
        Stream
    {
        byte[] pending = Encoding.UTF8.GetBytes(first);
        bool released;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, Cancel cancel = default)
        {
            if (pending.Length == 0)
            {
                if (released)
                {
                    return 0;
                }

                await gate.WaitAsync(cancel);
                released = true;
                pending = Encoding.UTF8.GetBytes(rest);
            }

            var count = Math.Min(buffer.Length, pending.Length);
            pending.AsSpan(0, count).CopyTo(buffer.Span);
            pending = pending[count..];
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, Cancel cancel) =>
            ReadAsync(buffer.AsMemory(offset, count), cancel).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count, Cancel.None).GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Returns at most <paramref name="size"/> bytes per read.</summary>
    sealed class TrickleStream(byte[] bytes, int size) :
        MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, Cancel cancel = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, size)], cancel);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, Cancel cancel) =>
            base.ReadAsync(buffer, offset, Math.Min(count, size), cancel);

        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, size));
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
        await Assert.That(request.Headers.NonValidated["Accept"].ToString()).IsEqualTo("application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8");
        await Assert.That(request.Headers.GetValues("authorization").Single()).IsEqualTo("Bearer token");
        await Assert.That(request.Headers.GetValues("x-custom").Single()).IsEqualTo("value");
        await Assert.That(request.Content!.Headers.ContentType!.MediaType).IsEqualTo("application/json");
        // Null variables are omitted, names are camelCase.
        await Assert.That(handler.RequestBody).IsEqualTo("""{"query":"{ id }","operationName":"Op"}""");
    }

    [Test]
    public async Task CapturesStatusAndYieldsErrorsOnNonSuccessJson()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.BadRequest, """{"errors":[{"message":"boom"}]}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        await Assert.That(fetcher.LastStatus).IsNull();
        var results = await Collect(fetcher, new("{ id }"));

        // GraphQL errors ride non-200s as ordinary documents.
        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].GetProperty("errors")[0].GetProperty("message").GetString()).IsEqualTo("boom");
        await Assert.That(fetcher.LastStatus).IsEqualTo(new(400, "Bad Request"));
    }

    [Test]
    public async Task NonJsonBodyThrows()
    {
        var longTail = new string('x', 600);
        var handler = new FakeHandler(_ => new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent($"<html>gateway fell over {longTail}</html>", Encoding.UTF8, "text/html")
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(fetcher, new("{ id }")));

        await Assert.That(exception!.Message).Contains("502").And.Contains("<html>gateway fell over");
        // Only the first 500 characters of the body are reported.
        await Assert.That(exception.Message).DoesNotContain("</html>");
        await Assert.That(fetcher.LastStatus!.StatusCode).IsEqualTo(502);
    }

    /// <summary>
    /// A body that only starts with JSON is not JSON. Go's net/http answers an unknown route with
    /// "404 page not found", which read as the number 404 and showed as a successful response.
    /// </summary>
    [Test]
    [Arguments("404 page not found")]
    [Arguments("""{"data":null} trailing""")]
    [Arguments("""{"data":null}{"data":null}""")]
    public async Task ABodyThatOnlyStartsWithJsonThrows(string body)
    {
        var handler = new FakeHandler(_ => new(HttpStatusCode.NotFound)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(fetcher, new("{ id }")));

        await Assert.That(exception!.Message).Contains("404").And.Contains(body);
    }

    /// <summary>Whitespace after the document is insignificant, the way a trailing newline is.</summary>
    [Test]
    public async Task TrailingWhitespaceIsStillJson()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"data\":{\"id\":\"abc123\"}}\r\n  \n"));
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ id }"));

        await Assert.That(results[0].GetProperty("data").GetProperty("id").GetString()).IsEqualTo("abc123");
    }

    /// <summary>A server that writes a UTF-8 byte order mark first is still sending JSON.</summary>
    [Test]
    public async Task AByteOrderMarkIsSkipped()
    {
        var bytes = (byte[]) [0xEF, 0xBB, 0xBF, .. """{"data":{"id":"abc123"}}"""u8.ToArray()];
        var handler = new FakeHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers =
                {
                    ContentType = new("application/json")
                }
            }
        });
        var fetcher = new HttpFetcher(new(handler), url);

        var results = await Collect(fetcher, new("{ id }"));

        await Assert.That(results[0].GetProperty("data").GetProperty("id").GetString()).IsEqualTo("abc123");
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

    /// <summary>
    /// TryAddWithoutValidation refuses a content header on the request's own collection, and says
    /// nothing about it — so a user-supplied Content-Type used to vanish without a word.
    /// </summary>
    [Test]
    public async Task AUserContentTypeReplacesTheDefault()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":null}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        await Collect(
            fetcher,
            new("{ id }"),
            new()
            {
                ["Content-Type"] = "application/graphql-request+json"
            });

        var content = handler.Request!.Content!;

        await Assert.That(content.Headers.GetValues("Content-Type").Single()).IsEqualTo("application/graphql-request+json");
        await Assert.That(handler.RequestBody).IsEqualTo("""{"query":"{ id }"}""");
    }

    [Test]
    public async Task AUserAcceptReplacesTheNegotiatedOne()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":null}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        await Collect(
            fetcher,
            new("{ id }"),
            new()
            {
                ["accept"] = "application/json"
            });

        await Assert.That(handler.Request!.Headers.GetValues("Accept").Single()).IsEqualTo("application/json");
    }

    [Test]
    public async Task TheNegotiatedAcceptStandsWhenTheUserSuppliesNone()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":null}"""));
        var fetcher = new HttpFetcher(new(handler), url);

        await Collect(fetcher, new("{ id }"), new() {["x-custom"] = "value"});

        await Assert.That(handler.Request!.Headers.NonValidated["Accept"].ToString()).IsEqualTo("application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8");
    }

    /// <summary>
    /// Streaming costs an interop round trip per chunk in the browser, so only a request that can
    /// be answered in parts asks for it: a document with @defer or @stream, or an Accept the user
    /// typed that names multipart.
    /// </summary>
    [Test]
    [Arguments("{ id }", null, false)]
    [Arguments("{ a { b ... @defer { c } } }", null, true)]
    [Arguments("{ list @stream(initialCount: 1) { id } }", null, true)]
    [Arguments("{ a { ... @ , defer { c } } }", null, true)]
    [Arguments("{ a @deferred }", null, false)]
    [Arguments("{ id }", "multipart/mixed;subscriptionSpec=1.0", true)]
    [Arguments("{ id }", "application/json", false)]
    public async Task StreamsOnlyWhatCanArriveInParts(string query, string? accept, bool streams)
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, """{"data":null}"""));
        var fetcher = new HttpFetcher(new(handler), url);
        var headers = new Dictionary<string, string>();
        if (accept is not null)
        {
            headers["Accept"] = accept;
        }

        await Collect(fetcher, new(query), headers);

        handler.Request!.Options.TryGetValue(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), out var streaming);
        await Assert.That(streaming).IsEqualTo(streams);
    }
}
