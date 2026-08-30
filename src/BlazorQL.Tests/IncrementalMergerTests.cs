/// <summary>
/// The incremental-delivery merge, ported from GraphiQL: the older path-based format, the newer
/// pending/completed id format, @stream items, error accumulation, and the replace semantics of
/// plain results (a subscription event replaces; a patch merges).
/// </summary>
[TestFixture]
public class IncrementalMergerTests
{
    [Test]
    public Task PlainResultReplaces()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"message":"Hi"}}"""));
        merger.Add(Parse("""{"data":{"message":"Bonjour"}}"""));

        return Verify(merger.Render());
    }

    [Test]
    public Task PathFormatDeferMerges()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"person":{"name":"Mark"}},"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"data":{"age":42},"path":["person"]}],"hasNext":false}"""));

        return Verify(merger.Render());
    }

    [Test]
    public Task IdFormatDeferMerges()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"deferrable":{"normalString":"Nice"}},"pending":[{"id":"0","path":["deferrable"]}],"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"id":"0","data":{"deferredString":"later"}}],"completed":[{"id":"0"}],"hasNext":false}"""));

        return Verify(merger.Render());
    }

    [Test]
    public Task IdFormatStreamAppends()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"streamable":[]},"pending":[{"id":"0","path":["streamable"]}],"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"id":"0","items":[{"text":"Hi"}]}],"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"id":"0","items":[{"text":"Hola"}]}],"completed":[{"id":"0"}],"hasNext":false}"""));

        return Verify(merger.Render());
    }

    [Test]
    public Task PathFormatStreamWritesAtIndices()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"streamable":[{"text":"Hi"}]},"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"items":[{"text":"Hola"},{"text":"Ciao"}],"path":["streamable",1]}],"hasNext":false}"""));

        return Verify(merger.Render());
    }

    [Test]
    public Task ErrorsAccumulate()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{"a":1},"pending":[{"id":"0","path":[]}],"hasNext":true}"""));
        merger.Add(Parse("""{"incremental":[{"id":"0","data":{"b":2},"errors":[{"message":"first"}]}],"hasNext":true}"""));
        merger.Add(Parse("""{"completed":[{"id":"0","errors":[{"message":"second"}]}],"hasNext":false}"""));

        return Verify(merger.Render());
    }

    [Test]
    public void UnknownIdIsRefused()
    {
        var merger = new IncrementalMerger();
        merger.Add(Parse("""{"data":{},"hasNext":true}"""));

        Assert.Throws<InvalidOperationException>(
            () => merger.Add(Parse("""{"incremental":[{"id":"9","data":{"x":1}}],"hasNext":false}""")));
    }

    static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;
}
