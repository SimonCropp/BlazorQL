
public class SmokeTests
{
    // Placeholder until the first real store lands: proves the test pipeline itself.
    [Test]
    public async Task SolutionBuildsAndTestsRun() =>
        await Assert.That(typeof(BlazorQLIde).Assembly.GetName().Name).IsEqualTo("BlazorQL");
}