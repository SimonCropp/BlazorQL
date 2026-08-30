[TestFixture]
public class TypeRefTests
{
    static TypeRef Named(string name) =>
        new()
        {
            Kind = "SCALAR",
            Name = name
        };

    static TypeRef NonNull(TypeRef inner) =>
        new()
        {
            Kind = "NON_NULL",
            OfType = inner
        };

    static TypeRef List(TypeRef inner) =>
        new()
        {
            Kind = "LIST",
            OfType = inner
        };

    [Test]
    public void DisplayRendersTheFullNesting()
    {
        Assert.That(Named("String").Display(), Is.EqualTo("String"));
        Assert.That(NonNull(Named("String")).Display(), Is.EqualTo("String!"));
        Assert.That(List(Named("Int")).Display(), Is.EqualTo("[Int]"));
        Assert.That(List(NonNull(Named("Int"))).Display(), Is.EqualTo("[Int!]"));
        Assert.That(NonNull(List(Named("Foo"))).Display(), Is.EqualTo("[Foo]!"));
        Assert.That(NonNull(List(NonNull(Named("Foo")))).Display(), Is.EqualTo("[Foo!]!"));
        Assert.That(List(List(Named("Foo"))).Display(), Is.EqualTo("[[Foo]]"));
    }

    [Test]
    public void UnwrapReachesTheNamedType()
    {
        var wrapped = NonNull(List(NonNull(Named("Foo"))));
        Assert.That(wrapped.Unwrap().Name, Is.EqualTo("Foo"));
        Assert.That(Named("Bar").Unwrap().Name, Is.EqualTo("Bar"));
    }
}
