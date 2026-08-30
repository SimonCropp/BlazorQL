/// <summary>
/// Publishes the sample once per test run. The browser fixtures serve this published output — the
/// exact static site GitHub Pages hosts — rather than the dev server, so what the tests prove is
/// what deploys.
/// </summary>
[SetUpFixture]
public class PublishedSample
{
    public static string WwwRoot { get; private set; } = null!;

    static string publishDirectory = null!;

    [OneTimeSetUp]
    public void Publish()
    {
        // .../tests/BlazorQL.Sample.Tests/bin/<config>/<tfm>/ — mirror <config> onto the publish.
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = baseDirectory.Parent!.Name;

        var directory = baseDirectory;
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "samples")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
        }

        var project = Path.Combine(directory.FullName, "samples", "BlazorQL.Sample", "BlazorQL.Sample.csproj");
        publishDirectory = Directory.CreateTempSubdirectory("blazorql_publish_").FullName;

        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("publish");
        info.ArgumentList.Add(project);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(configuration);
        info.ArgumentList.Add("--no-build");
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add(publishDirectory);

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new($"dotnet publish failed:\n{output}");
        }

        WwwRoot = Path.Combine(publishDirectory, "wwwroot");
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(publishDirectory, recursive: true);
        }
        catch (Exception)
        {
            // Best-effort cleanup of a temp directory; a lingering file lock is not a test failure.
        }
    }
}
