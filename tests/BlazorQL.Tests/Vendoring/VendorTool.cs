using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

/// <summary>
/// Produces <c>src/BlazorQL/wwwroot/vendor</c>: downloads the pinned npm tarballs and the esbuild
/// binary, lays them out as a node_modules, and bundles the entry files from
/// <c>src/BlazorQL/js</c>. Bundling is what enforces the one-Monaco-instance rule — separately
/// built monaco modules each carry their own language registry and never see each other. A
/// <c>manifest.json</c> records the pins and output hashes for provenance and drift tests.
/// </summary>
static class VendorTool
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNameCaseInsensitive = true
    };

    public static string VendorDirectory =>
        Path.Combine(RepositoryRoot(), "src", "BlazorQL", "wwwroot", "vendor");

    public static string EntriesDirectory =>
        Path.Combine(RepositoryRoot(), "src", "BlazorQL", "js");

    public static string ManifestPath =>
        Path.Combine(VendorDirectory, "manifest.json");

    public static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    // Tarballs and the esbuild binary are cached across runs; only the bundles are rebuilt.
    static string CacheDirectory =>
        Path.Combine(Path.GetTempPath(), "blazorql-vendor");

    public static async Task<SortedDictionary<string, string>> Refresh()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BlazorQL-vendor");

        var modules = Path.Combine(CacheDirectory, "node_modules");
        foreach (var (package, version) in VendorRoots.Packages)
        {
            await ExtractPackage(http, package, version, Path.Combine(modules, package));
        }

        var esbuildDirectory = Path.Combine(CacheDirectory, $"esbuild-{VendorRoots.EsbuildVersion}");
        await ExtractPackage(http, "@esbuild/win32-x64", VendorRoots.EsbuildVersion, esbuildDirectory);
        var esbuild = Path.Combine(esbuildDirectory, "esbuild.exe");

        var directory = VendorDirectory;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);

        foreach (var (entry, output) in VendorRoots.Entries)
        {
            Bundle(esbuild, modules, Path.Combine(EntriesDirectory, entry), Path.Combine(directory, output));
        }

        foreach (var (file, url) in VendorRoots.RawFiles)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, file), await http.GetStringAsync(url));
        }

        var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (package, version) in VendorRoots.Packages)
        {
            manifest[$"npm:{package}"] = version;
        }

        manifest["npm:@esbuild/win32-x64"] = VendorRoots.EsbuildVersion;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            manifest[$"file:{relative}"] = Sha256(await File.ReadAllBytesAsync(path));
        }

        await File.WriteAllTextAsync(ManifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        manifest[$"file:{Path.GetFileName(ManifestPath)}"] = "";
        return manifest;
    }

    static async Task ExtractPackage(HttpClient http, string package, string version, string destination)
    {
        if (Directory.Exists(destination))
        {
            return;
        }

        // Scoped packages tarball-name by the bare segment: @esbuild/win32-x64 → win32-x64-{v}.tgz.
        var bare = package.Split('/')[^1];
        var url = $"https://registry.npmjs.org/{package}/-/{bare}-{version}.tgz";
        await using var download = await http.GetStreamAsync(url);
        await using var gzip = new GZipStream(download, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);

        while (await tar.GetNextEntryAsync() is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile)
            {
                continue;
            }

            // Entries are rooted at "package/"; strip it.
            var relative = entry.Name[(entry.Name.IndexOf('/') + 1)..];
            var path = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await entry.ExtractToFileAsync(path, overwrite: true);
        }
    }

    static void Bundle(string esbuild, string modules, string entry, string output)
    {
        var arguments = string.Join(
            ' ',
            $"\"{entry}\"",
            "--bundle",
            "--format=esm",
            "--platform=browser",
            "--target=es2022",
            "--minify",
            "--loader:.ttf=file",
            "--define:process.env.NODE_ENV=\\\"production\\\"",
            $"--outfile=\"{output}\"");

        var info = new ProcessStartInfo(esbuild, arguments)
        {
            // Module resolution starts beside the entry; NODE_PATH supplies the assembled packages.
            EnvironmentVariables = {["NODE_PATH"] = modules},
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new($"esbuild failed for {Path.GetFileName(entry)}:\n{error}");
        }
    }

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}
