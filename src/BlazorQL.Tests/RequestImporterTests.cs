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

    /// <summary>
    /// What Chrome's "Copy as PowerShell" actually writes whenever the request carried a User-Agent,
    /// which is always: a web session built first, the user agent and cookies set on it, and only
    /// then the Invoke-WebRequest that uses it. The paste never started with the cmdlet, so it was
    /// never recognised.
    /// </summary>
    [Test]
    public async Task APowerShellCommandWithItsSessionPreambleImports()
    {
        var (ok, requests, error) = RequestImporter.Import(powerShellWithSession);
        var (_, plain, _) = RequestImporter.Import(powerShell);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0]).IsEqualTo(plain[0]);
    }

    /// <summary>
    /// DevTools writes every character outside printable ASCII as a <c>$([char]N)</c> subexpression,
    /// one per UTF-16 unit, and wraps such a body in UTF8.GetBytes. The wrapper was read as a bare
    /// word, and without it the subexpression went into the query as written.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task APowerShellBodyOutsideAsciiImportsItsCharacters(bool wrapped)
    {
        var body = "\"{`\"query`\":`\"{ search(term: \\`\"Jos$([char]233) $([char]55357)$([char]56832)\\`\") { id } }`\"}\"";
        var pasted =
            $"""
             Invoke-WebRequest -UseBasicParsing -Uri "https://example.com/graphql" `
             -Method "POST" `
             -ContentType "application/json" `
             -Body {(wrapped ? $"([System.Text.Encoding]::UTF8.GetBytes({body}))" : body)}
             """;

        var (ok, requests, error) = RequestImporter.Import(pasted);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("\"José 😀\"");
    }

    [Test]
    public async Task AFetchSnippetImportsTheBodysMutation()
    {
        var (ok, requests, error) = RequestImporter.Import(fetchCall);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await VerifyRequest(requests[0]);
    }

    /// <summary>
    /// A query that itself says <c>fetch(</c>, as a refetch field or one called fetch does, is not a
    /// fetch snippet. Any paste containing it went to the snippet reader and came back without a query.
    /// </summary>
    [Test]
    [Arguments("""{"query":"{ product(id: 1) { refetch(policy: NETWORK) { id } } }"}""")]
    [Arguments("""{"query":"{ fetch(id: 1) { id } }"}""")]
    [Arguments("https://x/graphql?query=%7B%20product(id%3A%201)%20%7B%20refetch(policy%3A%20NETWORK)%20%7B%20id%20%7D%20%7D%20%7D")]
    [Arguments("x/graphql?query=%7B%20product(id%3A%201)%20%7B%20refetch(policy%3A%20NETWORK)%20%7B%20id%20%7D%20%7D%20%7D")]
    public async Task AQueryThatSaysFetchIsNotAFetchSnippet(string pasted)
    {
        var (ok, requests, error) = RequestImporter.Import(pasted);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("fetch(");
    }

    /// <summary>The snippet forms a person writes rather than copies still read as snippets.</summary>
    [Test]
    [Arguments("await ")]
    [Arguments("const response = await window.")]
    public async Task AFetchCallAfterOtherCodeIsStillASnippet(string before)
    {
        var (ok, requests, error) = RequestImporter.Import(before + fetchCall);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("EnableUser");
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

    /// <summary>
    /// Eight hex digits do not fit an int: <c>\UFFFFFFFF</c> came out as -1, which slipped past the
    /// range check and threw. Parsing runs on every keystroke in the import dialog, so typing one
    /// out was enough. Past the last code point there is no character to decode to, so it is the
    /// replacement character, the way any invalid sequence is shown.
    /// </summary>
    [Test]
    [Arguments(@"\UFFFFFFFF")]
    [Arguments(@"\U80000000")]
    [Arguments(@"\U00110000")]
    public async Task AnEscapePastTheLastCodePointIsTheReplacementCharacter(string escape)
    {
        var tokens = ShellTokenizer.TokenizeBash($"curl 'https://x/graphql' -H $'x-a: {escape}'");

        await Assert.That(tokens[^1]).IsEqualTo("x-a: �");
    }

    /// <summary>The whole import holds to "nothing here throws" for one too.</summary>
    [Test]
    public async Task AnImportCarryingAnOutOfRangeEscapeDoesNotThrow()
    {
        var (ok, _, _) = RequestImporter.Import(
            """curl 'https://x/graphql' -H $'x-a: \UFFFFFFFF' --data-raw '{"query":"{ id }"}'""");

        await Assert.That(ok).IsTrue();
    }

    [Test]
    [Arguments(@"\U0001F600", "😀")]
    [Arguments(@"\U0010FFFF", "\U0010FFFF")]
    [Arguments(@"é", "é")]
    [Arguments(@"\x41", "A")]
    public async Task AnEscapeInRangeIsItsCharacter(string escape, string expected)
    {
        var tokens = ShellTokenizer.TokenizeBash($"curl 'https://x/graphql' -H $'x-a: {escape}'");

        await Assert.That(tokens[^1]).IsEqualTo($"x-a: {expected}");
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
    /// encodeURIComponent writes a plus sign as <c>%2B</c>, and that is still a plus once decoded.
    /// </summary>
    [Test]
    public async Task AnEncodedPlusSignInAUrlIsAPlus()
    {
        var (_, requests, _) = RequestImporter.Import(
            "https://host/graphql?query=query%20A%7Bid%7D&variables=%7B%22at%22%3A%222026-07-30T04%3A26%3A09%2B10%3A00%22%7D");

        await Assert.That(requests[0].Variables).Contains("+10:00");
    }

    /// <summary>
    /// A raw plus only comes from form-style encoding (URLSearchParams, an HTML form, jQuery, Java's
    /// URLEncoder), where it is a space. Read as a plus, every space in the query was a syntax error.
    /// </summary>
    [Test]
    public async Task ARawPlusSignInAUrlIsASpace()
    {
        var (ok, requests, error) = RequestImporter.Import(
            "https://x/graphql?query=query+GetUser+%7B+user+%7B+id+%7D+%7D&operationName=GetUser");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("query GetUser");
    }

    /// <summary>A form-encoded body is read by the same rules, so a curl posting one imports too.</summary>
    [Test]
    public async Task AFormEncodedBodyWithPlusSignsImports()
    {
        var (ok, requests, error) = RequestImporter.Import(
            "curl 'https://x/graphql' -H 'content-type: application/x-www-form-urlencoded' --data-raw 'query=query+GetUser+%7B+user+%7B+id+%7D+%7D&variables=%7B%22at%22%3A%2210%2B2%22%7D'");

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(requests[0].Query).Contains("query GetUser");
        // An encoded plus in the same body is still a plus.
        await Assert.That(requests[0].Variables).Contains("10+2");
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

    /// <summary>
    /// Both ways a query arrives: inside a JSON body, and as the body itself — which starts with a
    /// brace, so it is tried as JSON first and as a document second.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AQueryNestedTooDeepToParseIsRefusedRatherThanThrown(bool inJson)
    {
        var text = inJson
            ? $$"""{"query":"{{DocumentInfoTests.TooDeep}}"}"""
            : DocumentInfoTests.TooDeep;

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

    const string powerShellWithSession =
        """
        $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
        $session.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/152.0.0.0 Safari/537.36"
        $session.Cookies.Add((New-Object System.Net.Cookie("legislation", "JWT", "/", "legislation.dfdev.lab")))
        Invoke-WebRequest -UseBasicParsing -Uri "https://legislation.dfdev.lab/graphql" `
        -Method "POST" `
        -WebSession $session `
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