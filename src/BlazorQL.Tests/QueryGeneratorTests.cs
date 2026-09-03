/// <summary>
/// The documentation explorer's generate-query output, over the same canned schema the explorer
/// tests use. Every generated document must also survive prettify unchanged, so the generator's
/// layout and the formatter's agree.
/// </summary>
[TestFixture]
public class QueryGeneratorTests
{
    static readonly SchemaIndex schema = LoadSchema();

    static SchemaIndex LoadSchema()
    {
        var json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "DocExplorerTests.schema.json"));
        return Parse(json);
    }

    static SchemaIndex Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    static string Generate(SchemaIndex index, string typeName)
    {
        var generated = QueryGenerator.Generate(index, index.Find(typeName)!)!;
        // The printer writes the platform newline; the generator always writes \n.
        var formatted = Formatter.FormatGraphQL(generated).Replace("\r\n", "\n");
        Assert.That(formatted, Is.EqualTo(generated), "prettify should be a no-op");
        return generated;
    }

    // The root type becomes the operation of its kind, with every non-deprecated field.
    [Test]
    public Task RootType() =>
        Verify(Generate(schema, "Query"));

    // A type a root field returns is fetched through that field; nested composites take the
    // leaf-filler's default choice.
    [Test]
    public Task TypeThroughRootField() =>
        Verify(Generate(schema, "Person"));

    // A union selects an inline fragment per member type.
    [Test]
    public Task UnionThroughRootField() =>
        Verify(Generate(schema, "SearchResult"));

    // A type no root field returns becomes a fragment.
    [Test]
    public Task UnreachableTypeBecomesAFragment() =>
        Verify(Generate(schema, "Post"));

    [Test]
    public Task InterfaceBecomesAFragment() =>
        Verify(Generate(schema, "Named"));

    [Test]
    public void LeafAndInputTypesGenerateNothing()
    {
        Assert.That(QueryGenerator.CanGenerate(schema.Find("Color")!), Is.False);
        Assert.That(QueryGenerator.CanGenerate(schema.Find("PetInput")!), Is.False);
        Assert.That(QueryGenerator.CanGenerate(schema.Find("String")!), Is.False);
        Assert.That(QueryGenerator.Generate(schema, schema.Find("Color")!), Is.Null);
    }

    // The reported case: a type no root field returns, but something nested does. Nesting the
    // selection under the chain that reaches it produces a document that runs - a fragment alone
    // answers "Document does not contain any operations".
    [Test]
    public Task NestedOnlyTypeIsReachedThroughItsChain() =>
        Verify(Generate(Nested(), "Portfolio"));

    // The chain is the shortest one, and each step keeps the arguments it requires.
    [Test]
    public void TheChainIsTheShortestThatReaches()
    {
        var index = Nested();

        var path = index.PathFromQuery("Portfolio");

        string[] toPortfolio = ["groups", "portfolios"];
        string[] toGroup = ["groups"];
        Assert.That(path, Is.Not.Null);
        Assert.That(path!.Select(_ => _.Name), Is.EqualTo(toPortfolio).AsCollection);
        Assert.That(index.PathFromQuery("Group")!.Select(_ => _.Name), Is.EqualTo(toGroup).AsCollection);
    }

    // Nothing reaches it, so there is no operation to build and the fragment stands.
    [Test]
    public void AnUnreachableTypeHasNoChain() =>
        Assert.That(Nested().PathFromQuery("Orphan"), Is.Null);

    static SchemaIndex Nested() =>
        Parse(
            """
            {
              "__schema": {
                "queryType": { "name": "Query" },
                "types": [
                  {
                    "kind": "OBJECT",
                    "name": "Query",
                    "fields": [
                      { "name": "groups", "args": [], "type": { "kind": "LIST", "ofType": { "kind": "OBJECT", "name": "Group" } } }
                    ]
                  },
                  {
                    "kind": "OBJECT",
                    "name": "Group",
                    "fields": [
                      { "name": "id", "args": [], "type": { "kind": "SCALAR", "name": "ID" } },
                      {
                        "name": "portfolios",
                        "args": [
                          { "name": "take", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "Int" } } }
                        ],
                        "type": { "kind": "LIST", "ofType": { "kind": "OBJECT", "name": "Portfolio" } }
                      }
                    ]
                  },
                  {
                    "kind": "OBJECT",
                    "name": "Portfolio",
                    "fields": [
                      { "name": "id", "args": [], "type": { "kind": "SCALAR", "name": "ID" } },
                      { "name": "title", "args": [], "type": { "kind": "SCALAR", "name": "String" } }
                    ]
                  },
                  {
                    "kind": "OBJECT",
                    "name": "Orphan",
                    "fields": [
                      { "name": "id", "args": [], "type": { "kind": "SCALAR", "name": "ID" } }
                    ]
                  },
                  { "kind": "SCALAR", "name": "ID" },
                  { "kind": "SCALAR", "name": "Int" },
                  { "kind": "SCALAR", "name": "String" }
                ],
                "directives": []
              }
            }
            """);

    // Required arguments (non-null without a default) become variables named after the argument;
    // a second argument with the same name is prefixed with its field. Optional arguments and
    // arguments with defaults are left out.
    [Test]
    public Task RequiredArgumentsBecomeVariables()
    {
        var index = Parse(
            """
            {
              "__schema": {
                "queryType": { "name": "Query" },
                "types": [
                  {
                    "kind": "OBJECT",
                    "name": "Query",
                    "fields": [
                      {
                        "name": "node",
                        "args": [
                          { "name": "id", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "ID" } } }
                        ],
                        "type": { "kind": "OBJECT", "name": "User" }
                      },
                      {
                        "name": "user",
                        "args": [
                          { "name": "id", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "ID" } } },
                          { "name": "includeDeleted", "type": { "kind": "SCALAR", "name": "Boolean" } },
                          { "name": "first", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "Int" } }, "defaultValue": "10" }
                        ],
                        "type": { "kind": "OBJECT", "name": "User" }
                      },
                      {
                        "name": "users",
                        "args": [
                          { "name": "ids", "type": { "kind": "NON_NULL", "ofType": { "kind": "LIST", "ofType": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "ID" } } } } }
                        ],
                        "type": { "kind": "LIST", "ofType": { "kind": "OBJECT", "name": "User" } }
                      }
                    ]
                  },
                  {
                    "kind": "OBJECT",
                    "name": "User",
                    "fields": [
                      { "name": "id", "args": [], "type": { "kind": "SCALAR", "name": "ID" } },
                      {
                        "name": "avatar",
                        "args": [
                          { "name": "size", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "Int" } } }
                        ],
                        "type": { "kind": "SCALAR", "name": "String" }
                      }
                    ]
                  },
                  { "kind": "SCALAR", "name": "ID" },
                  { "kind": "SCALAR", "name": "Int" },
                  { "kind": "SCALAR", "name": "String" },
                  { "kind": "SCALAR", "name": "Boolean" }
                ],
                "directives": []
              }
            }
            """);
        return Verify(Generate(index, "User"));
    }

    // A composite field whose type has nothing selectable at the depth limit is dropped, and the
    // variables reserved for its arguments go with it.
    [Test]
    public Task UnselectableCompositeIsDroppedWithItsVariables()
    {
        var index = Parse(
            """
            {
              "__schema": {
                "queryType": { "name": "Query" },
                "types": [
                  {
                    "kind": "OBJECT",
                    "name": "Query",
                    "fields": [
                      { "name": "name", "args": [], "type": { "kind": "SCALAR", "name": "String" } },
                      {
                        "name": "empty",
                        "args": [
                          { "name": "key", "type": { "kind": "NON_NULL", "ofType": { "kind": "SCALAR", "name": "String" } } }
                        ],
                        "type": { "kind": "OBJECT", "name": "Empty" }
                      }
                    ]
                  },
                  {
                    "kind": "OBJECT",
                    "name": "Empty",
                    "fields": [
                      { "name": "inner", "args": [], "type": { "kind": "OBJECT", "name": "Hollow" } }
                    ]
                  },
                  { "kind": "OBJECT", "name": "Hollow", "fields": [] },
                  { "kind": "SCALAR", "name": "String" }
                ],
                "directives": []
              }
            }
            """);
        return Verify(Generate(index, "Query"));
    }
}
