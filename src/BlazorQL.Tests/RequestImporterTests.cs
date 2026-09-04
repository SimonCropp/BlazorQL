/// <summary>
/// Importing a request copied out of a browser's network tab. The fixtures are real devtools output
/// rather than tidied-up equivalents: the escaping is the whole problem, and anything hand-written
/// is neater than what Chrome actually emits. Cookie and token values are the only edits, shortened
/// because their length is not what any of these tests are about.
/// </summary>
[TestFixture]
public class RequestImporterTests
{
    [Test]
    public Task ARawGetUrlImportsItsQueryAndOperationName()
    {
        var (ok, requests, error) = RequestImporter.Import(getUrl);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    [Test]
    public Task ABashCurlImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(bashCurl);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    [Test]
    public Task ACmdCurlImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(cmdCurl);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    /// <summary>
    /// The two shells encode the same capture completely differently, so agreeing on the result is
    /// the strongest single check that either decoder is right.
    /// </summary>
    [Test]
    public void TheTwoShellFlavoursOfOneRequestImportIdentically()
    {
        var (_, fromBash, _) = RequestImporter.Import(bashCurl);
        var (_, fromCmd, _) = RequestImporter.Import(cmdCurl);

        Assert.That(fromCmd[0], Is.EqualTo(fromBash[0]));
    }

    [Test]
    public Task APowerShellCommandImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(powerShell);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    [Test]
    public Task AFetchSnippetImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(fetchCall);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    [Test]
    public Task ABareJsonBodyImportsItsOperation()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """{"operationName":"EnableUser","variables":{"id":"a"},"query":"mutation EnableUser($id:ID!){enableUser(id:$id){success}}"}""");

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return VerifyRequest(requests[0]);
    }

    /// <summary>
    /// A quote inside a value is encoded as caret-backslash-caret-quote, which shares its first two
    /// characters with the argument delimiter. A decoder that matches the delimiter first splits the
    /// value in half here.
    /// </summary>
    [Test]
    public void AQuotedHeaderValueSurvivesTheCmdEncoding()
    {
        // Named x- rather than sec- so it survives the denylist and can be asserted through the
        // public entry point rather than only against the tokenizer.
        var (_, requests, _) = RequestImporter.Import(
            """curl --url ^"http://localhost/graphql^" -H ^"x-ch-ua: ^\^"Chromium^\^";v=^\^"152^\^", ^\^"Not?A_Brand^\^";v=^\^"24^\^"^" --data-raw ^"^{^\^"query^\^":^\^"^{id^}^\^"^}^" """);

        using var headers = JsonDocument.Parse(requests[0].Headers);

        Assert.That(
            headers.RootElement.GetProperty("x-ch-ua").GetString(),
            Is.EqualTo("""
                       "Chromium";v="152", "Not?A_Brand";v="24"
                       """));
    }

    /// <summary>
    /// Chrome emits one literal backslash as caret-backslash-caret-backslash. A decoder that only
    /// knows the escaped-quote form doubles every backslash in a value instead.
    /// </summary>
    [Test]
    public void ALiteralBackslashSurvivesTheCmdEncoding()
    {
        var tokens = ShellTokenizer.TokenizeCmd(
            """curl --url ^"http://localhost^" -b ^"^\^\^\^\attacker.com^\^\share^\^\leak=foo^" """);

        Assert.That(tokens[^1], Is.EqualTo(@"\\attacker.com\share\leak=foo"));
    }

    /// <summary>
    /// Bash switches to ANSI-C quoting whenever a value holds a control character, which is not a
    /// rare shape: devtools uses it for any body or cookie carrying a newline.
    /// </summary>
    [Test]
    public void AnsiCQuotingIsDecoded()
    {
        var tokens = ShellTokenizer.TokenizeBash("""curl --url 'http://localhost' -b $'query=evil\r\n & calc \u0021'""");

        Assert.That(tokens[^1], Is.EqualTo("query=evil\r\n & calc !"));
    }

    /// <summary>Chrome's syntax for a header it captured with no value at all.</summary>
    [Test]
    public void ABareHeaderNameWithATrailingSemicolonIsNotAFailure()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """curl --url 'http://localhost/graphql' -H 'x-trace;' --data-raw '{"query":"{ id }"}'""");

        Assert.That(error, Is.Null);
        Assert.That(ok);
        Assert.That(requests[0].Headers, Does.Contain("x-trace"));
    }

    /// <summary>
    /// A GET carries "variables={}" even when there are none. Writing that into the pane would pop
    /// the tools strip open over an empty object.
    /// </summary>
    [Test]
    public void AnEmptyVariablesObjectLeavesTheVariablesPaneEmpty()
    {
        var (_, requests, _) = RequestImporter.Import("https://host/graphql?variables=%7B%7D&query=query%20A%7Bid%7D");

        Assert.That(requests[0].Variables, Is.Empty);
    }

    /// <summary>
    /// Clients build these urls with encodeURIComponent, which leaves a plus alone — so decoding one
    /// as form encoding would turn data into whitespace.
    /// </summary>
    [Test]
    public void APlusSignInAUrlIsNotASpace()
    {
        var (_, requests, _) = RequestImporter.Import(
            "https://host/graphql?query=query%20A%7Bid%7D&variables=%7B%22at%22%3A%222026-07-30T04%3A26%3A09%2B10%3A00%22%7D");

        Assert.That(requests[0].Variables, Does.Contain("+10:00"));
    }

    [Test]
    public void BrowserControlledHeadersAreDropped()
    {
        var (_, requests, _) = RequestImporter.Import(cmdCurl);
        var request = requests[0];

        Assert.Multiple(() =>
        {
            Assert.That(request.Headers, Does.Contain("df-client-version"));
            Assert.That(request.Headers, Does.Contain("df-client-commit-hash"));
            // The session token, the client hints, and everything else the browser owns.
            Assert.That(request.Headers, Does.Not.Contain("cookie"));
            Assert.That(request.Headers, Does.Not.Contain("sec-ch-ua"));
            Assert.That(request.Headers, Does.Not.Contain("user-agent"));
            Assert.That(request.Headers, Does.Not.Contain("origin"));
            // Accept is negotiated by the fetcher; an imported one disables incremental delivery.
            Assert.That(request.Headers, Does.Not.Contain("accept"));
            Assert.That(request.HeadersImported, Is.EqualTo(2));
            Assert.That(request.HeadersFound, Is.EqualTo(19));
        });
    }

    [Test]
    public void AnAuthorizationHeaderIsKept()
    {
        var (_, requests, _) = RequestImporter.Import(
            """curl --url 'http://localhost/graphql' -H 'authorization: Bearer abc' --data-raw '{"query":"{ id }"}'""");

        Assert.That(requests[0].Headers, Does.Contain("Bearer abc"));
    }

    [Test]
    public void APersistedQueryReportsThatTheDocumentCannotBeRecovered()
    {
        var (ok, _, error) = RequestImporter.Import(
            "https://host/graphql?operationName=A&extensions=%7B%22persistedQuery%22%3A%7B%22sha256Hash%22%3A%22abc%22%7D%7D");

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo(RequestBodyReader.PersistedQuery));
    }

    /// <summary>
    /// The tab's operation name exists to disambiguate, and pinning it on a single-operation
    /// document would be state the document already carries.
    /// </summary>
    [Test]
    public void ASingleOperationDoesNotPinTheOperationName()
    {
        var (_, requests, _) = RequestImporter.Import(
            """{"operationName":"A","query":"query A{id}"}""");

        Assert.That(requests[0].OperationName, Is.Null);
    }

    /// <summary>
    /// With more than one operation the name is what scopes the variables check, so it has to be
    /// pinned or the pane validates against the wrong declarations.
    /// </summary>
    [Test]
    public void AMultiOperationDocumentPinsTheOperationName()
    {
        var (_, requests, _) = RequestImporter.Import(
            """{"operationName":"B","query":"query A{id} query B($x:Int){other(x:$x)}"}""");

        Assert.That(requests[0].OperationName, Is.EqualTo("B"));
    }

    [Test]
    public void ABatchedBodyBecomesOneRequestEach()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """[{"query":"query A{a}"},{"query":"query B{b}"},{"query":"query C{c}"}]""");

        Assert.That(error, Is.Null);
        Assert.That(ok);
        Assert.That(requests, Has.Count.EqualTo(3));
        Assert.That(requests[2].Query, Does.Contain("C"));
    }

    /// <summary>A brace opens both a JSON object and an anonymous query; only one of them parses.</summary>
    [Test]
    public void ADocumentPastedOnItsOwnIsStillImported()
    {
        var (ok, requests, error) = RequestImporter.Import("{ hero { name } }");

        Assert.That(error, Is.Null);
        Assert.That(ok);
        Assert.That(requests[0].Query, Does.Contain("hero"));
    }

    [Test]
    public void AMarkdownFenceAroundThePasteIsIgnored()
    {
        var (ok, _, error) = RequestImporter.Import(
            $"```bash\n{bashCurl}\n```");

        Assert.That(error, Is.Null);
        Assert.That(ok);
    }

    [Test]
    public void AShellPromptBeforeThePasteIsIgnored()
    {
        var (ok, _, error) = RequestImporter.Import($"$ {bashCurl}");

        Assert.That(error, Is.Null);
        Assert.That(ok);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("hello world")]
    [TestCase("curl")]
    [TestCase("curl --url")]
    [TestCase("""curl --url ^" """)]
    [TestCase("fetch(")]
    [TestCase("{")]
    [TestCase("[")]
    [TestCase("$'")]
    [TestCase("https://")]
    [TestCase("https://host/graphql")]
    [TestCase("Invoke-WebRequest -Headers @{")]
    [TestCase("""{"variables":{}}""")]
    public void MalformedInputIsRefusedRatherThanThrown(string text)
    {
        var (ok, requests, error) = RequestImporter.Import(text);

        Assert.That(ok, Is.False);
        Assert.That(requests, Is.Empty);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    static Task VerifyRequest(ImportedRequest request) =>
        Verify(
            $"""
             operation: {request.OperationName ?? "<not pinned>"}
             headers:   {request.HeadersImported} of {request.HeadersFound}

             ---- query ----
             {Normalize(request.Query)}
             ---- variables ----
             {Normalize(request.Variables)}
             ---- headers ----
             {Normalize(request.Headers)}
             """);

    // GraphQLParser's printer emits the platform's newline, so the snapshots would differ per OS.
    static string Normalize(string text) =>
        text.Replace("\r\n", "\n");

    const string getUrl =
        "https://legislation.dfdev.lab/graphql?operationName=CurrentUserPermissions&variables=%7B%7D&query=query%20CurrentUserPermissions%7BcanViewBill%7B...CanObject%20__typename%7DcurrentUser%7Bid%20firstName%20lastName%20__typename%7D%7Dfragment%20CanObject%20on%20Can%7Ballowed%20userHasRights%20reasonDenied%20__typename%7D";

    const string bashCurl =
        """
        curl --url 'https://legislation.dfdev.lab/graphql' \
          -H 'accept: application/json, text/plain, */*' \
          -H 'accept-language: en-AU,en;q=0.9,en-US;q=0.8' \
          -H 'cache-control: no-cache' \
          -H 'content-type: application/json' \
          -b 'ai_user=+qdLrL0UNm6JIE5D7rXxaL|2026-07-30T04:26:09.540Z; legislation=JWT' \
          -H 'df-client-commit-hash: 694c0b5c' \
          -H 'df-client-version: 1.0.5233' \
          -H 'origin: https://legislation.dfdev.lab' \
          -H 'pragma: no-cache' \
          -H 'priority: u=1, i' \
          -H 'referer: https://legislation.dfdev.lab/admin/users/view-inactive-user/bca79fdd' \
          -H 'sec-ch-ua: "Chromium";v="152", "Not?A_Brand";v="24", "Google Chrome";v="152"' \
          -H 'sec-ch-ua-mobile: ?0' \
          -H 'sec-ch-ua-platform: "Windows"' \
          -H 'sec-fetch-dest: empty' \
          -H 'sec-fetch-mode: cors' \
          -H 'sec-fetch-site: same-origin' \
          -H 'traceparent: 00-4c4983c950ae4cdba9aaedf335ad5fbd-9da3ce7eff854262-01' \
          -H 'user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/152.0.0.0 Safari/537.36' \
          --data-raw '{"operationName":"EnableUser","variables":{"input":{"id":"bca79fdd-5157-3fc9-1892-afd65d64eebb","rowVersion":76863}},"query":"mutation EnableUser($input:EnableUserStateInput){enableUser(input:$input){success __typename}}"}'
        """;

    const string cmdCurl =
        """
        curl --url ^"https://legislation.dfdev.lab/graphql^" ^
          -H ^"accept: application/json, text/plain, */*^" ^
          -H ^"accept-language: en-AU,en;q=0.9,en-US;q=0.8^" ^
          -H ^"cache-control: no-cache^" ^
          -H ^"content-type: application/json^" ^
          -b ^"ai_user=+qdLrL0UNm6JIE5D7rXxaL^|2026-07-30T04:26:09.540Z; legislation=JWT^" ^
          -H ^"df-client-commit-hash: 694c0b5c^" ^
          -H ^"df-client-version: 1.0.5233^" ^
          -H ^"origin: https://legislation.dfdev.lab^" ^
          -H ^"pragma: no-cache^" ^
          -H ^"priority: u=1, i^" ^
          -H ^"referer: https://legislation.dfdev.lab/admin/users/view-inactive-user/bca79fdd^" ^
          -H ^"sec-ch-ua: ^\^"Chromium^\^";v=^\^"152^\^", ^\^"Not?A_Brand^\^";v=^\^"24^\^", ^\^"Google Chrome^\^";v=^\^"152^\^"^" ^
          -H ^"sec-ch-ua-mobile: ?0^" ^
          -H ^"sec-ch-ua-platform: ^\^"Windows^\^"^" ^
          -H ^"sec-fetch-dest: empty^" ^
          -H ^"sec-fetch-mode: cors^" ^
          -H ^"sec-fetch-site: same-origin^" ^
          -H ^"traceparent: 00-4c4983c950ae4cdba9aaedf335ad5fbd-9da3ce7eff854262-01^" ^
          -H ^"user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/152.0.0.0 Safari/537.36^" ^
          --data-raw ^"^{^\^"operationName^\^":^\^"EnableUser^\^",^\^"variables^\^":^{^\^"input^\^":^{^\^"id^\^":^\^"bca79fdd-5157-3fc9-1892-afd65d64eebb^\^",^\^"rowVersion^\^":76863^}^},^\^"query^\^":^\^"mutation EnableUser(^$input:EnableUserStateInput)^{enableUser(input:^$input)^{success __typename^}^}^\^"^}^"
        """;

    const string powerShell =
        """"
        Invoke-WebRequest -UseBasicParsing -Uri "https://legislation.dfdev.lab/graphql" `
        -Method "POST" `
        -Headers @{
          "content-type"="application/json"
          "df-client-version"="1.0.5233"
          "authorization"="Bearer abc"
        } `
        -Body "{`"operationName`":`"EnableUser`",`"variables`":{`"input`":{`"id`":`"bca79fdd`"}},`"query`":`"mutation EnableUser(`$input:EnableUserStateInput){enableUser(input:`$input){success __typename}}`"}"
        """";

    const string fetchCall =
        """"
        fetch("https://legislation.dfdev.lab/graphql", {
          "headers": {
            "accept": "application/json, text/plain, */*",
            "content-type": "application/json",
            "df-client-version": "1.0.5233"
          },
          "body": "{\"operationName\":\"EnableUser\",\"variables\":{\"input\":{\"id\":\"bca79fdd\"}},\"query\":\"mutation EnableUser($input:EnableUserStateInput){enableUser(input:$input){success __typename}}\"}",
          "method": "POST",
          "mode": "cors",
          "credentials": "include"
        });
        """";
}
