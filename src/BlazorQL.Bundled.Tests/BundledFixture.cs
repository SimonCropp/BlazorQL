using System.Collections.Concurrent;
using BlazorQL;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Interfaces;

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
    WebApplication host = null!;
    IPlaywright playwright = null!;
    IBrowser browser = null!;

    readonly ConcurrentQueue<string> console = new();

    /// <summary>The origin the app is served at, path base included, no trailing slash.</summary>
    protected string BaseUrl { get; private set; } = null!;

    /// <summary>Where the IDE is mounted, no trailing slash. Empty mounts it at the root.</summary>
    protected string IdeUrl => BaseUrl + Mount.TrimEnd('/');

    /// <summary>Sub-path the whole app is mounted under, as a reverse proxy would.</summary>
    protected virtual string PathBase => "";

    /// <summary>The pattern passed to MapBlazorQL.</summary>
    protected virtual string Mount => "/graphql-ide";

    /// <summary>Turns on the consumer's own response compression, which must not double-encode.</summary>
    protected virtual bool UseResponseCompression => false;

    protected virtual void Configure(BlazorQLIdeOptions options)
    {
    }

    [OneTimeSetUp]
    public async Task Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        if (UseResponseCompression)
        {
            builder.Services.AddResponseCompression(_ => _.EnableForHttps = true);
        }

        host = builder.Build();

        if (UseResponseCompression)
        {
            host.UseResponseCompression();
        }

        if (PathBase.Length > 0)
        {
            host.UsePathBase(PathBase);
        }

        host.MapSampleSchema();
        host.MapBlazorQL(Mount, Configure);

        await host.StartAsync();
        var origin = host.Urls.Single().TrimEnd('/');
        BaseUrl = origin + PathBase;

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(
            new()
            {
                // Grayscale text rather than LCD subpixel antialiasing: the colour fringing is not
                // stable between browser sessions, which is fatal to screenshot baselines.
                Args = ["--disable-lcd-text"]
            });
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

    [SetUp]
    public void ClearConsole() =>
        console.Clear();

    /// <summary>Reports what the page logged, but only for a test that failed.</summary>
    [TearDown]
    public void ReportConsoleOnFailure()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed ||
            console.IsEmpty)
        {
            return;
        }

        TestContext.Out.WriteLine($"Browser console during {TestContext.CurrentContext.Test.Name}:");
        foreach (var message in console)
        {
            TestContext.Out.WriteLine($"  {message}");
        }
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        playwright?.Dispose();

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }
}
