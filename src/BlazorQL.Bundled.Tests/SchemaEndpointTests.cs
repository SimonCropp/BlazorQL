/// <summary>
/// The fixture's own GraphQL endpoint, checked directly. If this fails, every browser test that
/// needs a schema fails as an unexplained timeout, so it is worth isolating.
/// </summary>
[TestFixture]
public class SchemaEndpointTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task ExecutesAQuery()
    {
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BaseUrl + "/graphql",
            new StringContent(
                """{"query":"{ id isTest }"}""",
                Encoding.UTF8,
                "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("abc123"));
    }

    [Test]
    public async Task AnswersIntrospection()
    {
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BaseUrl + "/graphql",
            new StringContent(
                """{"query":"{ __schema { queryType { name } } }"}""",
                Encoding.UTF8,
                "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("queryType"));
    }
}
