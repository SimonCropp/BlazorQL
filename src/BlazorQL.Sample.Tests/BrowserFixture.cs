using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

/// <summary>
/// Serves the published sample from an in-process static host and launches a headless Chromium.
/// <see cref="PathBase"/> lets a derived fixture mount the same output under a sub-path, proving
/// GitHub-Pages-style hosting (the site lives at /&lt;repo&gt;/) on every run.
/// </summary>
public abstract class BrowserFixture
{
    // TUnit builds a fresh instance per test, so what NUnit kept per fixture instance lives here:
    // one browser for the run, and one host per concrete class, started by its first test.
    static IPlaywright playwright = null!;
    static IBrowser browser = null!;
    static readonly ConcurrentDictionary<Type, Lazy<Task<WebApplication>>> hosts = new();

    // What the page logged during the current test. Written from Playwright's own threads, so a
    // concurrent collection rather than a List.
    readonly ConcurrentQueue<string> console = new();

    /// <summary>The url the app is served at, path base included, no trailing slash.</summary>
    protected string BaseUrl { get; private set; } = null!;

    /// <summary>Sub-path to mount the app under (for example <c>/BlazorQL</c>). Empty = root.</summary>
    protected virtual string PathBase => "";

    /// <summary>
    /// The Content-Security-Policy the host sends with every response. Null sends none, which is
    /// what most of the suite wants.
    /// </summary>
    protected virtual string? ContentSecurityPolicy => null;

    /// <summary>
    /// Opens a page, recording everything it logs for the duration of the test. The only way a
    /// test gets a page, so none can quietly opt out of the recording.
    /// </summary>
    protected async Task<IPage> NewPageAsync(BrowserNewPageOptions? options = null)
    {
        var page = await browser.NewPageAsync(options);
        page.Console += (_, message) => console.Enqueue($"[{message.Type}] {message.Text}");
        page.PageError += (_, error) => console.Enqueue($"[pageerror] {error}");
        // A failed asset is otherwise near-invisible: the AMD loader swallows a missing monaco
        // chunk into a bare "[object Event]" and the console message for a 404 never names the url.
        page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                console.Enqueue($"[error] {response.Status} {response.Url}");
            }
        };
        return page;
    }

    /// <summary>The errors the page logged so far — the canary for silent asset/worker failures.</summary>
    protected IReadOnlyList<string> ConsoleErrors() =>
        [.. console.Where(_ => _.StartsWith("[error]", StringComparison.Ordinal) || _.StartsWith("[pageerror]", StringComparison.Ordinal))];

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
        var host = await hosts.GetOrAdd(GetType(), _ => new(StartHost)).Value;
        BaseUrl = host.Urls.Single().TrimEnd('/') + PathBase;
    }

    async Task<WebApplication> StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var host = builder.Build();

        var files = new PhysicalFileProvider(PublishedSample.WwwRoot);
        var index = RewriteBaseHref(Path.Combine(PublishedSample.WwwRoot, "index.html"));

        if (PathBase.Length > 0)
        {
            host.UsePathBase(PathBase);
            // Everything else 404s, exactly as GitHub Pages would answer outside the repo path.
            host.Use((context, next) =>
            {
                if (!context.Request.PathBase.HasValue)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                return next();
            });
        }

        if (ContentSecurityPolicy is {Length: > 0} policy)
        {
            host.Use((context, next) =>
            {
                context.Response.Headers.ContentSecurityPolicy = policy;
                return next();
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
        return host;
    }

    string RewriteBaseHref(string indexPath)
    {
        var html = File.ReadAllText(indexPath);
        if (PathBase.Length == 0)
        {
            return html;
        }

        return html.Replace("<base href=\"/\" />", $"<base href=\"{PathBase}/\" />");
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
