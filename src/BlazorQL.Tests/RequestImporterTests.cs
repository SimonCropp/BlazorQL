/// <summary>
/// Importing a request copied out of a browser's network tab. The fixtures are real devtools output
/// rather than tidied-up equivalents: the escaping is the whole problem, and anything hand-written
/// is neater than what Chrome actually emits. Cookie and token values are the only edits, shortened
/// because their length is not what any of these tests are about.
/// </summary>
public class RequestImporterTests
{
    [Test]
    public async Task ARawGetUrlImportsItsQueryAndOperationName()
    {
        var (ok, requests, error) = RequestImporter.Import(getUrl);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    [Test]
    public async Task ABashCurlImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(bashCurl);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    [Test]
    public async Task ACmdCurlImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(cmdCurl);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    /// <summary>
    /// The two shells encode the same capture completely differently, so agreeing on the result is
    /// the strongest single check that either decoder is right.
    /// </summary>
    [Test]
    public async Task TheTwoShellFlavoursOfOneRequestImportIdentically()
    {
        var (_, fromBash, _) = RequestImporter.Import(bashCurl);
        var (_, fromCmd, _) = RequestImporter.Import(cmdCurl);

        await Assert.That(fromCmd[0]).IsEqualTo(fromBash[0]);
    }

    [Test]
    public async Task APowerShellCommandImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(powerShell);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    [Test]
    public async Task AFetchSnippetImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(fetchCall);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    [Test]
    public async Task ABareJsonBodyImportsItsOperation()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """{"operationName":"EnableUser","variables":{"id":"a"},"query":"mutation EnableUser($id:ID!){enableUser(id:$id){success}}"}""");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    /// <summary>
    /// A quote inside a value is encoded as caret-backslash-caret-quote, which shares its first two
    /// characters with the argument delimiter. A decoder that matches the delimiter first splits the
    /// value in half here.
    /// </summary>
    [Test]
    public async Task AQuotedHeaderValueSurvivesTheCmdEncoding()
    {
        // Named x- rather than sec- so it survives the denylist and can be asserted through the
        // public entry point rather than only against the tokenizer.
        var (_, requests, _) = RequestImporter.Import(
            """curl --url ^"http://localhost/graphql^" -H ^"x-ch-ua: ^\^"Chromium^\^";v=^\^"152^\^", ^\^"Not?A_Brand^\^";v=^\^"24^\^"^" --data-raw ^"^{^\^"query^\^":^\^"^{id^}^\^"^}^" """);

        using var headers = JsonDocument.Parse(requests[0].Headers);

        await Assert.That(headers.RootElement.GetProperty("x-ch-ua").GetString()).IsEqualTo("""
                       "Chromium";v="152", "Not?A_Brand";v="24"
                       """);
    }

    /// <summary>
    /// Chrome emits one literal backslash as caret-backslash-caret-backslash. A decoder that only
    /// knows the escaped-quote form doubles every backslash in a value instead.
    /// </summary>
    [Test]
    public async Task ALiteralBackslashSurvivesTheCmdEncoding()
    {
        var tokens = ShellTokenizer.TokenizeCmd(
            """curl --url ^"http://localhost^" -b ^"^\^\^\^\attacker.com^\^\share^\^\leak=foo^" """);

        await Assert.That(tokens[^1]).IsEqualTo(@"\\attacker.com\share\leak=foo");
    }

    /// <summary>
    /// Bash switches to ANSI-C quoting whenever a value holds a control character, which is not a
    /// rare shape: devtools uses it for any body or cookie carrying a newline.
    /// </summary>
    [Test]
    public async Task AnsiCQuotingIsDecoded()
    {
        var tokens = ShellTokenizer.TokenizeBash("""curl --url 'http://localhost' -b $'query=evil\r\n & calc \u0021'""");

        await Assert.That(tokens[^1]).IsEqualTo("query=evil\r\n & calc !");
    }

    /// <summary>Chrome's syntax for a header it captured with no value at all.</summary>
    [Test]
    public async Task ABareHeaderNameWithATrailingSemicolonIsNotAFailure()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """curl --url 'http://localhost/graphql' -H 'x-trace;' --data-raw '{"query":"{ id }"}'""");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Headers).Contains("x-trace");
    }

    /// <summary>
    /// A GET carries "variables={}" even when there are none. Writing that into the pane would pop
    /// the tools strip open over an empty object.
    /// </summary>
    [Test]
    public async Task AnEmptyVariablesObjectLeavesTheVariablesPaneEmpty()
    {
        var (_, requests, _) = RequestImporter.Import("https://host/graphql?variables=%7B%7D&query=query%20A%7Bid%7D");

        await Assert.That(requests[0].Variables).IsEmpty();
    }

    /// <summary>
    /// Clients build these urls with encodeURIComponent, which leaves a plus alone — so decoding one
    /// as form encoding would turn data into whitespace.
    /// </summary>
    [Test]
    public async Task APlusSignInAUrlIsNotASpace()
    {
        var (_, requests, _) = RequestImporter.Import(
            "https://host/graphql?query=query%20A%7Bid%7D&variables=%7B%22at%22%3A%222026-07-30T04%3A26%3A09%2B10%3A00%22%7D");

        await Assert.That(requests[0].Variables).Contains("+10:00");
    }

    [Test]
    public async Task BrowserControlledHeadersAreDropped()
    {
        var (_, requests, _) = RequestImporter.Import(cmdCurl);
        var request = requests[0];

        using (Assert.Multiple())
        {
            await Assert.That(request.Headers).Contains("df-client-version");
            await Assert.That(request.Headers).Contains("df-client-commit-hash");
            // The session token, the client hints, and everything else the browser owns.
            await Assert.That(request.Headers).DoesNotContain("cookie");
            await Assert.That(request.Headers).DoesNotContain("sec-ch-ua");
            await Assert.That(request.Headers).DoesNotContain("user-agent");
            await Assert.That(request.Headers).DoesNotContain("origin");
            // Accept is negotiated by the fetcher; an imported one disables incremental delivery.
            await Assert.That(request.Headers).DoesNotContain("accept");
            await Assert.That(request.HeadersImported).IsEqualTo(2);
            await Assert.That(request.HeadersFound).IsEqualTo(19);

        }
    }

    [Test]
    public async Task AnAuthorizationHeaderIsKept()
    {
        var (_, requests, _) = RequestImporter.Import(
            """curl --url 'http://localhost/graphql' -H 'authorization: Bearer abc' --data-raw '{"query":"{ id }"}'""");

        await Assert.That(requests[0].Headers).Contains("Bearer abc");
    }

    [Test]
    public async Task APersistedQueryReportsThatTheDocumentCannotBeRecovered()
    {
        var (ok, _, error) = RequestImporter.Import(
            "https://host/graphql?operationName=A&extensions=%7B%22persistedQuery%22%3A%7B%22sha256Hash%22%3A%22abc%22%7D%7D");

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo(RequestBodyReader.PersistedQuery);
    }

    /// <summary>
    /// The tab's operation name exists to disambiguate, and pinning it on a single-operation
    /// document would be state the document already carries.
    /// </summary>
    [Test]
    public async Task ASingleOperationDoesNotPinTheOperationName()
    {
        var (_, requests, _) = RequestImporter.Import(
            """{"operationName":"A","query":"query A{id}"}""");

        await Assert.That(requests[0].OperationName).IsNull();
    }

    /// <summary>
    /// With more than one operation the name is what scopes the variables check, so it has to be
    /// pinned or the pane validates against the wrong declarations.
    /// </summary>
    [Test]
    public async Task AMultiOperationDocumentPinsTheOperationName()
    {
        var (_, requests, _) = RequestImporter.Import(
            """{"operationName":"B","query":"query A{id} query B($x:Int){other(x:$x)}"}""");

        await Assert.That(requests[0].OperationName).IsEqualTo("B");
    }

    [Test]
    public async Task ABatchedBodyBecomesOneRequestEach()
    {
        var (ok, requests, error) = RequestImporter.Import(
            """[{"query":"query A{a}"},{"query":"query B{b}"},{"query":"query C{c}"}]""");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests).Count().IsEqualTo(3);
        await Assert.That(requests[2].Query).Contains("C");
    }

    /// <summary>A brace opens both a JSON object and an anonymous query; only one of them parses.</summary>
    [Test]
    public async Task ADocumentPastedOnItsOwnIsStillImported()
    {
        var (ok, requests, error) = RequestImporter.Import("{ hero { name } }");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("hero");
    }

    [Test]
    public async Task AMarkdownFenceAroundThePasteIsIgnored()
    {
        var (ok, _, error) = RequestImporter.Import(
            $"```bash\n{bashCurl}\n```");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
    }

    [Test]
    public async Task AShellPromptBeforeThePasteIsIgnored()
    {
        var (ok, _, error) = RequestImporter.Import($"$ {bashCurl}");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("hello world")]
    [Arguments("curl")]
    [Arguments("curl --url")]
    [Arguments("""curl --url ^" """)]
    [Arguments("fetch(")]
    [Arguments("{")]
    [Arguments("[")]
    [Arguments("$'")]
    [Arguments("https://")]
    [Arguments("https://host/graphql")]
    [Arguments("Invoke-WebRequest -Headers @{")]
    [Arguments("""{"variables":{}}""")]
    public async Task MalformedInputIsRefusedRatherThanThrown(string text)
    {
        var (ok, requests, error) = RequestImporter.Import(text);

        await Assert.That(ok).IsFalse();
        await Assert.That(requests).IsEmpty();
        await Assert.That(error).IsNotNullOrEmpty();
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
        """
        Invoke-WebRequest -UseBasicParsing -Uri "https://legislation.dfdev.lab/graphql" `
        -Method "POST" `
        -Headers @{
          "content-type"="application/json"
          "df-client-version"="1.0.5233"
          "authorization"="Bearer abc"
        } `
        -Body "{`"operationName`":`"EnableUser`",`"variables`":{`"input`":{`"id`":`"bca79fdd`"}},`"query`":`"mutation EnableUser(`$input:EnableUserStateInput){enableUser(input:`$input){success __typename}}`"}"
        """;

    const string fetchCall =
        """
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
        """;
}