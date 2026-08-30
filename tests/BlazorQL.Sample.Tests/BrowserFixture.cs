using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Interfaces;

/// <summary>
/// Serves the published sample from an in-process static host and launches a headless Chromium.
/// <see cref="PathBase"/> lets a derived fixture mount the same output under a sub-path, proving
/// GitHub-Pages-style hosting (the site lives at /&lt;repo&gt;/) on every run.
/// </summary>
public abstract class BrowserFixture
{
    WebApplication host = null!;
    IPlaywright playwright = null!;
    IBrowser browser = null!;

    // What the page logged during the current test. Written from Playwright's own threads, so a
    // concurrent collection rather than a List.
    readonly ConcurrentQueue<string> console = new();

    /// <summary>The url the app is served at, path base included, no trailing slash.</summary>
    protected string BaseUrl { get; private set; } = null!;

    /// <summary>Sub-path to mount the app under (for example <c>/BlazorQL</c>). Empty = root.</summary>
    protected virtual string PathBase => "";

    /// <summary>
    /// Opens a page, recording everything it logs for the duration of the test. The only way a
    /// test gets a page, so none can quietly opt out of the recording.
    /// </summary>
    protected async Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null)
    {
        var page = await browser.NewPageAsync(options);
        page.Console += (_, message) => console.Enqueue($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => console.Enqueue($"[pageerror] {error}");
        return page;
    }

    /// <summary>The errors the page logged so far — the canary for silent asset/worker failures.</summary>
    protected IReadOnlyList<string> ConsoleErrors() =>
        [.. console.Where(_ => _.StartsWith("[error]", StringComparison.Ordinal) || _.StartsWith("[pageerror]", StringComparison.Ordinal))];

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

    [OneTimeSetUp]
    public async Task Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        host = builder.Build();

        var files = new PhysicalFileProvider(PublishedSample.WwwRoot);
        var index = RewriteBaseHref(Path.Combine(PublishedSample.WwwRoot, "index.html"));

        if (PathBase.Length > 0)
        {
            host.UsePathBase(PathBase);
            // Everything else 404s, exactly as GitHub Pages would answer outside the repo path.
            host.Use(async (context, next) =>
            {
                if (!context.Request.PathBase.HasValue)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next();
            });
        }

        host.UseStaticFiles(new StaticFileOptions {FileProvider = files, ServeUnknownFileTypes = true});
        // Extensionless = a client-side route; serve the (base-href-rewritten) host page.
        host.MapFallback(context =>
        {
            context.Response.ContentType = "text/html";
            return context.Response.WriteAsync(index);
        });

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

    string RewriteBaseHref(string indexPath)
    {
        var html = File.ReadAllText(indexPath);
        return PathBase.Length == 0
            ? html
            : html.Replace("<base href=\"/\" />", $"<base href=\"{PathBase}/\" />");
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

        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        if (host is not null)
        {
            await host.DisposeAsync();
        }
    }
}
