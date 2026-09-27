using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// An ASP.NET Core app that serves a real GraphQL endpoint and mounts the IDE next to it with one
/// call to <see cref="BlazorQLIdeEndpointRouteBuilderExtensions.MapBlazorQL"/>.
/// </summary>
/// <remarks>
/// There is no publish step here, unlike the sample's fixture: the whole IDE already lives inside
/// the referenced assembly. That absence is the product claim, so the fixture is deliberately this
/// short.
/// <para>
/// The schema runs on the server, which is what the WebAssembly sample structurally cannot do — its
/// schema executes in the browser, so nothing there exercises browser to http to server and back.
/// </para>
/// </remarks>
public abstract class BundledFixture
{
    // TUnit builds a fresh instance per test, so what NUnit kept per fixture instance lives here:
    // one browser for the run, and one host per concrete class, started by its first test.
    static IPlaywright playwright = null!;
    static IBrowser browser = null!;
    static readonly ConcurrentDictionary<Type, Lazy<Task<WebApplication>>> hosts = new();

    WebApplication host = null!;

    readonly ConcurrentQueue<string> console = new();

    /// <summary>The origin the app is served at, path base included, no trailing slash.</summary>
    protected string BaseUrl { get; private set; } = null!;

    /// <summary>Where the IDE is mounted, no trailing slash. Empty mounts it at the root.</summary>
    protected string IdeUrl => BaseUrl + Mount.TrimEnd('/');

    /// <summary>
    /// What the mount names the app when it derives a storage namespace. Read from the host rather
    /// than assumed, because the entry assembly under a test runner is the runner's.
    /// </summary>
    protected string ApplicationName =>
        host.Services.GetRequiredService<IHostEnvironment>().ApplicationName;

    /// <summary>Sub-path the whole app is mounted under, as a reverse proxy would.</summary>
    protected virtual string PathBase => "";

    /// <summary>The pattern passed to MapBlazorQL.</summary>
    protected virtual string Mount => "/blazorql";

    /// <summary>
    /// Mount through the overload that takes only the configuration, leaving the pattern to the
    /// package. <see cref="Mount"/> then has to agree with what the package defaults to.
    /// </summary>
    protected virtual bool MountAtDefault => false;

    /// <summary>Turns on the consumer's own response compression, which must not double-encode.</summary>
    protected virtual bool UseResponseCompression => false;

    /// <summary>The key the host stashes a request's CSP nonce under.</summary>
    protected const string NonceKey = "CspNonce";

    /// <summary>
    /// The Content-Security-Policy the host sets on every response, with <c>{nonce}</c> standing in
    /// for the request's nonce. Null sends no policy, which is what most of the suite wants.
    /// </summary>
    protected virtual string? ContentSecurityPolicy => null;

    protected virtual void Configure(BlazorQLIdeOptions options)
    {
    }

    /// <summary>The GraphQL endpoint the IDE talks to. Overridden to put a gate in front of it.</summary>
    protected virtual void MapSchema(WebApplication app) =>
        app.MapSampleSchema();

    [Before(TestSession)]
    public static async Task LaunchBrowser()
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(
            new()
            {
                // Grayscale text rather than LCD subpixel antialiasing: the colour fringing is not
                // stable between browser sessions, which is fatal to screenshot baselines.
                Args = ["--disable-lcd-text"]
            });
    }

    [Before(Test)]
    public async Task Start()
    {
        console.Clear();
        host = await hosts.GetOrAdd(GetType(), _ => new(StartHost)).Value;
        BaseUrl = host.Urls.Single().TrimEnd('/') + PathBase;
    }

    async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (UseResponseCompression)
        {
            builder.Services.AddResponseCompression(_ => _.EnableForHttps = true);
        }

        var app = builder.Build();

        if (UseResponseCompression)
        {
            app.UseResponseCompression();
        }

        if (PathBase.Length > 0)
        {
            app.UsePathBase(PathBase);
        }

        if (ContentSecurityPolicy is {Length: > 0} policy)
        {
            app.Use((context, next) =>
            {
                var nonce = RandomNumberGenerator.GetHexString(32);
                context.Items[NonceKey] = nonce;
                context.Response.Headers.ContentSecurityPolicy = policy.Replace("{nonce}", nonce);
                return next();
            });
        }

        MapSchema(app);
        if (MountAtDefault)
        {
            app.MapBlazorQL(Configure);
        }
        else
        {
            app.MapBlazorQL(Mount, Configure);
        }

        await app.StartAsync();
        return app;
    }

    /// <summary>
    /// Opens a page, recording everything it logs for the duration of the test. The only way a test
    /// gets a page, so none can quietly opt out of the recording.
    /// </summary>
    protected async Task<IPage> NewPageAsync()
    {
        var page = await browser.NewPageAsync();
        page.Console += (_, message) => console.Enqueue($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => console.Enqueue($"[pageerror] {error}");
        // A missing embedded asset is otherwise near-invisible: the AMD loader swallows a failed
        // monaco chunk into a bare "[object Event]", and the console message for a 404 never names
        // the url.
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                console.Enqueue($"[error] {response.Status} {response.Url}");
            }
        };
        return page;
    }

    /// <summary>The errors the page logged so far — the canary for silent asset failures.</summary>
    protected IReadOnlyList<string> ConsoleErrors() =>
        [.. console.Where(_ => _.StartsWith("[error]", StringComparison.Ordinal) || _.StartsWith("[pageerror]", StringComparison.Ordinal))];

    /// <summary>Opens the mounted IDE and waits until it is usable.</summary>
    protected async Task<IPage> OpenIdeAsync()
    {
        var page = await NewPageAsync();
        await page.GotoAsync(IdeUrl + "/");
        await page.WaitForSelectorAsync(".monaco-editor", new() {Timeout = 60_000});
        await page.WaitForSelectorAsync("[data-testid='blazorql'][data-ready]", new() {Timeout = 90_000});
        return page;
    }

    /// <summary>Reports what the page logged, but only for a test that failed.</summary>
    [After(Test)]
    public void ReportConsoleOnFailure(TestContext context)
    {
        if (context.Execution.Result?.State != TestState.Failed ||
            console.IsEmpty)
        {
            return;
        }

        context.Output.WriteLine($"Browser console during {context.Metadata.TestName}:");
        foreach (var message in console)
        {
            context.Output.WriteLine($"  {message}");
        }
    }

    [After(TestSession)]
    public static async Task Stop()
    {
        foreach (var started in hosts.Values)
        {
            if (started.IsValueCreated &&
                started.Value.IsCompletedSuccessfully)
            {
                await started.Value.Result.DisposeAsync();
            }
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        playwright?.Dispose();
    }
}
