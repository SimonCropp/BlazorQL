/// <summary>
/// The string the session header renders beside the logo. Nothing else in this suite can render
/// that header - Monaco needs a browser - so the value is asserted here, and its placement in the
/// sample's browser suite.
/// </summary>
public class IdeVersionTests
{
    [Test]
    public async Task TheVersionIsTheAssemblysWithoutTheCommit()
    {
        var version = IdeVersion.Current;

        // Not AssemblyVersion, which is pinned at 1.0.0 so a consumer's binding never breaks on a
        // release, and not the informational version raw - SourceLink appends "+{commit}" to that,
        // and a header has no use for forty characters of hash.
        await Assert.That(version).Matches(@"^\d+\.\d+\.\d+");
        await Assert.That(version).DoesNotContain("+");
    }
}