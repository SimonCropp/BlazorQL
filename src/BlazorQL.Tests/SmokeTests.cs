[TestFixture]
public class SmokeTests
{
    // Placeholder until the first real store lands: proves the test pipeline itself.
    [Test]
    public void SolutionBuildsAndTestsRun() =>
        Assert.That(typeof(BlazorQLIde).Assembly.GetName().Name, Is.EqualTo("BlazorQL"));
}
