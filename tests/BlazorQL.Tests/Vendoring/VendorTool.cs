using System.Security.Cryptography;
using System.Text.RegularExpressions;

/// <summary>
/// Downloads the pinned esm.sh module graph into <c>src/BlazorQL/wwwroot/vendor</c>, rewriting every
/// absolute specifier to a relative one so the vendored files load from static hosting with no import
/// map, no bundler, and no network. Bare <c>"graphql"</c> specifiers (what <c>external=graphql</c>
/// emits) are rewritten to the single vendored graphql module — the one-instance rule enforced at
/// vendor time. A <c>manifest.json</c> records url → file → sha256 for provenance and drift tests.
/// </summary>
static partial class VendorTool
{
    [GeneratedRegex("""(?<=\bfrom\s*)["'](?<spec>[^"']+)["']""")]
    private static partial Regex FromSpecifiers();

    [GeneratedRegex("""(?<=\bimport\s*)["'](?<spec>[^"']+)["']""")]
    private static partial Regex SideEffectSpecifiers();

    [GeneratedRegex("""(?<=\bimport\(\s*)["'](?<spec>[^"']+)["']""")]
    private static partial Regex DynamicSpecifiers();

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string VendorDirectory =>
        Path.Combine(RepositoryRoot(), "src", "BlazorQL", "wwwroot", "vendor");

    public static string ManifestPath =>
        Path.Combine(VendorDirectory, "manifest.json");

    public static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    public static async Task<SortedDictionary<string, VendorEntry>> Refresh()
    {
        var directory = VendorDirectory;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BlazorQL-vendor");

        // url -> local path relative to vendor/, forward slashes.
        var localPaths = new Dictionary<string, string>();
        var contents = new Dictionary<string, string>();
        var queue = new Queue<string>();

        foreach (var (file, url) in VendorRoots.Modules)
        {
            localPaths[url] = file;
            queue.Enqueue(url);
        }

        while (queue.Count > 0)
        {
            var url = queue.Dequeue();
            if (contents.ContainsKey(url))
            {
                continue;
            }

            string text;
            try
            {
                text = await http.GetStringAsync(url);
            }
            catch (HttpRequestException exception) when (
                exception.StatusCode == HttpStatusCode.NotFound &&
                !VendorRoots.Modules.Values.Contains(url))
            {
                // The specifier patterns also match path-shaped strings inside minified code; one
                // that 404s is such junk, and its "import" is left as it was. A root 404 is real.
                localPaths.Remove(url);
                continue;
            }
            catch (HttpRequestException exception)
            {
                throw new($"Fetching {url} failed: {exception.Message}", exception);
            }

            contents[url] = text;

            foreach (var specifier in Specifiers(text))
            {
                var resolved = Resolve(specifier, url);
                if (resolved is null)
                {
                    continue;
                }

                if (!localPaths.ContainsKey(resolved))
                {
                    localPaths[resolved] = LocalPathFor(resolved);
                }

                queue.Enqueue(resolved);
            }
        }

        var manifest = new SortedDictionary<string, VendorEntry>(StringComparer.Ordinal);
        foreach (var (url, text) in contents)
        {
            var local = localPaths[url];
            var rewritten = Rewrite(text, url, local, localPaths);
            var fullPath = Path.Combine(directory, local.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, rewritten);
            manifest[local] = new(url, Sha256(rewritten));
        }

        foreach (var (file, url) in VendorRoots.RawFiles)
        {
            var text = await http.GetStringAsync(url);
            await File.WriteAllTextAsync(Path.Combine(directory, file), text);
            manifest[file] = new(url, Sha256(text));
        }

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(ManifestPath, json);
        return manifest;
    }

    [GeneratedRegex("^[A-Za-z0-9@_/.:+^~-]+$")]
    private static partial Regex PathLike();

    public static IEnumerable<string> Specifiers(string text) =>
        FromSpecifiers().Matches(text)
            .Concat(SideEffectSpecifiers().Matches(text))
            .Concat(DynamicSpecifiers().Matches(text))
            .Select(_ => _.Groups["spec"].Value)
            // Minified code trips the patterns on strings that merely follow the keywords (a
            // tokenizer's keyword list, say); a real specifier is path-shaped.
            .Where(_ => PathLike().IsMatch(_))
            .Distinct();

    /// <summary>The absolute url a specifier refers to, or null when it needs no fetch.</summary>
    static string? Resolve(string specifier, string fromUrl)
    {
        if (specifier.StartsWith("https://esm.sh/", StringComparison.Ordinal))
        {
            return specifier;
        }

        if (specifier.StartsWith('/'))
        {
            return "https://esm.sh" + specifier;
        }

        if (specifier is "graphql")
        {
            // Rewritten to the vendored module; nothing new to fetch.
            return null;
        }

        if (specifier.StartsWith('.'))
        {
            // Relative within the same esm.sh build directory; resolve against the importing file.
            return new Uri(new(fromUrl), specifier).ToString();
        }

        // A bare specifier other than the graphql external. The patterns also match import-shaped
        // strings inside minified code (a parser's error-message samples, say), so this cannot be
        // fatal — a genuinely missing module surfaces in the browser smoke test's console instead.
        return null;
    }

    static string Rewrite(string text, string url, string local, Dictionary<string, string> localPaths)
    {
        string Replace(Match match)
        {
            var specifier = match.Groups["spec"].Value;
            if (!PathLike().IsMatch(specifier))
            {
                return match.Value;
            }

            if (specifier is "graphql")
            {
                return '"' + RelativePath(local, "graphql.js") + '"';
            }

            var resolved = Resolve(specifier, url);
            if (resolved is null ||
                !localPaths.TryGetValue(resolved, out var target))
            {
                return match.Value;
            }

            return '"' + RelativePath(local, target) + '"';
        }

        text = FromSpecifiers().Replace(text, Replace);
        text = SideEffectSpecifiers().Replace(text, Replace);
        return DynamicSpecifiers().Replace(text, Replace);
    }

    /// <summary>Relative path from one vendored file to another, in browser form.</summary>
    public static string RelativePath(string from, string to)
    {
        var fromDirectory = Path.GetDirectoryName(from)?.Replace('\\', '/') ?? "";
        var relative = Path.GetRelativePath(
                fromDirectory.Length == 0 ? "." : fromDirectory,
                to)
            .Replace('\\', '/');
        return relative.StartsWith('.') ? relative : "./" + relative;
    }

    /// <summary>A vendor-relative file path derived from the url path, Windows-safe.</summary>
    static string LocalPathFor(string url)
    {
        var path = new Uri(url).AbsolutePath.TrimStart('/');
        // esm.sh uses '*' to mark all-external builds; invalid in Windows paths.
        return path.Replace("*", "X-");
    }

    public static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

record VendorEntry(string Url, string Sha256);
