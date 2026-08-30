using System.Security.Cryptography;

/// <summary>
/// The vendored JS layer: refreshed by the explicit test (network), guarded by the normal ones
/// (offline). Builds never touch the network — the vendored files are committed, and drift between
/// them and the manifest fails the suite.
/// </summary>
[TestFixture]
public class VendorTests
{
    /// <summary>
    /// Re-downloads the pinned esm.sh closure and overwrites <c>wwwroot/vendor</c>. Run it to
    /// refresh or to verify currency: a clean git diff afterwards means the assets are current.
    /// </summary>
    [Test]
    [Explicit("Network: fetches the pinned esm.sh closure and overwrites the committed vendor directory.")]
    public async Task RefreshVendoredAssets()
    {
        var manifest = await VendorTool.Refresh();

        Assert.That(manifest, Is.Not.Empty);
        TestContext.Out.WriteLine($"{manifest.Count} files vendored to {VendorTool.VendorDirectory}. Review the git diff.");
    }

    [Test]
    public void EveryManifestEntryMatchesTheFileOnDisk()
    {
        var manifest = Manifest();
        Assert.That(manifest, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (file, entry) in manifest)
            {
                var path = Path.Combine(VendorTool.VendorDirectory, file);
                Assert.That(File.Exists(path), Is.True, $"{file} is in the manifest but not on disk.");
                Assert.That(
                    VendorTool.Sha256(File.ReadAllText(path)),
                    Is.EqualTo(entry.Sha256),
                    $"{file} does not match the manifest. Run RefreshVendoredAssets, or revert the edit.");
            }
        });
    }

    [Test]
    public void NoVendoredFileEscapesTheVendorDirectory() =>
        Assert.Multiple(() =>
        {
            foreach (var path in Directory.EnumerateFiles(VendorTool.VendorDirectory, "*.js", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                Assert.That(text, Does.Not.Contain("https://esm.sh"), $"{Name(path)} still fetches from esm.sh.");
                foreach (var specifier in VendorTool.Specifiers(text))
                {
                    // Bare tokens can be import-shaped strings inside minified code, so only the
                    // shapes that would actually leave the vendor directory fail here.
                    Assert.That(
                        specifier,
                        Does.Not.StartWith("/").And.Not.StartWith("http"),
                        $"{Name(path)} imports '{specifier}', which is not vendored-relative.");
                }
            }
        });

    static string Name(string path) =>
        Path.GetRelativePath(VendorTool.VendorDirectory, path).Replace('\\', '/');

    static SortedDictionary<string, VendorEntry> Manifest() =>
        JsonSerializer.Deserialize<SortedDictionary<string, VendorEntry>>(
            File.ReadAllText(VendorTool.ManifestPath),
            VendorTool.JsonOptions)!;
}
