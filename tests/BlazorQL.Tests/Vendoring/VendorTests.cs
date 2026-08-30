/// <summary>
/// The vendored JS layer: refreshed by the explicit test (network + esbuild), guarded by the
/// normal one (offline). Builds never touch the network — the vendored bundles are committed, and
/// drift between them and the manifest fails the suite.
/// </summary>
[TestFixture]
public class VendorTests
{
    /// <summary>
    /// Re-downloads the pinned npm packages, re-bundles the entries, and overwrites
    /// <c>wwwroot/vendor</c>. Run it to refresh or to verify currency: a clean git diff afterwards
    /// means the assets are current.
    /// </summary>
    [Test]
    [Explicit("Network: downloads the pinned npm closure and overwrites the committed vendor directory.")]
    public async Task RefreshVendoredAssets()
    {
        var manifest = await VendorTool.Refresh();

        Assert.That(manifest, Is.Not.Empty);
        TestContext.Out.WriteLine($"{manifest.Count} entries written to {VendorTool.VendorDirectory}. Review the git diff.");
    }

    [Test]
    public void EveryManifestEntryMatchesTheFileOnDisk()
    {
        var manifest = JsonSerializer.Deserialize<SortedDictionary<string, string>>(
            File.ReadAllText(VendorTool.ManifestPath),
            VendorTool.JsonOptions)!;

        var files = manifest.Where(_ => _.Key.StartsWith("file:", StringComparison.Ordinal)).ToList();
        Assert.That(files, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (key, sha) in files)
            {
                var file = key["file:".Length..];
                var path = Path.Combine(VendorTool.VendorDirectory, file);
                Assert.That(File.Exists(path), Is.True, $"{file} is in the manifest but not on disk.");
                Assert.That(
                    VendorTool.Sha256(File.ReadAllBytes(path)),
                    Is.EqualTo(sha),
                    $"{file} does not match the manifest. Run RefreshVendoredAssets, or revert the edit.");
            }
        });
    }
}
