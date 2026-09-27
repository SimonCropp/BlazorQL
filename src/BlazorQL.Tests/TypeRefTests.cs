
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
    public async Task DisplayRendersTheFullNesting()
    {
        await Assert.That(Named("String").Display()).IsEqualTo("String");
        await Assert.That(NonNull(Named("String")).Display()).IsEqualTo("String!");
        await Assert.That(List(Named("Int")).Display()).IsEqualTo("[Int]");
        await Assert.That(List(NonNull(Named("Int"))).Display()).IsEqualTo("[Int!]");
        await Assert.That(NonNull(List(Named("Foo"))).Display()).IsEqualTo("[Foo]!");
        await Assert.That(NonNull(List(NonNull(Named("Foo")))).Display()).IsEqualTo("[Foo!]!");
        await Assert.That(List(List(Named("Foo"))).Display()).IsEqualTo("[[Foo]]");
    }

    [Test]
    public async Task UnwrapReachesTheNamedType()
    {
        var wrapped = NonNull(List(NonNull(Named("Foo"))));
        await Assert.That(wrapped.Unwrap().Name).IsEqualTo("Foo");
        await Assert.That(Named("Bar").Unwrap().Name).IsEqualTo("Bar");
    }
}