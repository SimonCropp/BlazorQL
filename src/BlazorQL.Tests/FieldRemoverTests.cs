/// <summary>
/// Removing the field an error points at. The cases that matter are the ones where a naive text
/// delete leaves a document the server will not take: an emptied selection set, an orphaned
/// variable, a name that appears in more than one place.
/// </summary>
[TestFixture]
public class FieldRemoverTests
{
    [Test]
    public void RemovesATopLevelField()
    {
        var text =
            """
            query {
              accessGroup {
                id
              }
              accessGroups {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        Assert.That(
            result,
            Is.EqualTo(
                """
                query {
                  accessGroups {
                    id
                  }
                }
                """));
    }

    [Test]
    public void RemovesANestedField()
    {
        var text =
            """
            query {
              accessGroups {
                id
                members {
                  id
                }
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroups", "members"]);

        Assert.That(
            result,
            Is.EqualTo(
                """
                query {
                  accessGroups {
                    id
                  }
                }
                """));
    }

    /// <summary>
    /// The path carries indices for list elements; the document mentions the selection set once.
    /// </summary>
    [Test]
    public void IgnoresListIndicesInThePath()
    {
        var text =
            """
            query {
              accessGroups {
                id
                members {
                  id
                }
              }
            }
            """;

        // What ResponseErrors hands over for a path of ["accessGroups", 0, "members"].
        var result = FieldRemover.Remove(text, ["accessGroups", "members"]);

        Assert.That(result, Does.Not.Contain("members"));
        Assert.That(result, Does.Contain("accessGroups"));
    }

    /// <summary>The path names response keys, so an alias is what a segment matches.</summary>
    [Test]
    public void MatchesOnTheAlias()
    {
        var text =
            """
            query {
              first: accessGroup {
                id
              }
              second: accessGroup {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["second"]);

        Assert.That(result, Does.Contain("first: accessGroup"));
        Assert.That(result, Does.Not.Contain("second"));
    }

    /// <summary>Emptied braces do not parse, so the parent goes as well.</summary>
    [Test]
    public void TakesTheParentWhenItWouldBeLeftEmpty()
    {
        var text =
            """
            query {
              accessGroup {
                members {
                  id
                }
              }
              other
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup", "members", "id"]);

        Assert.That(
            result,
            Is.EqualTo(
                """
                query {
                  other
                }
                """));
    }

    /// <summary>
    /// Nothing would be left to select, so there is no removal that leaves a valid document and the
    /// action reports that rather than producing one.
    /// </summary>
    [Test]
    public void RefusesWhenTheOperationWouldBeEmptied()
    {
        var text =
            """
            query {
              accessGroup {
                id
              }
            }
            """;

        Assert.That(FieldRemover.Remove(text, ["accessGroup"]), Is.Null);
        Assert.That(FieldRemover.Remove(text, ["accessGroup", "id"]), Is.Null);
    }

    /// <summary>An unused variable is a validation error, so removing its last use removes it.</summary>
    [Test]
    public void DropsAVariableNothingUsesAnyMore()
    {
        var text =
            """
            query Groups($id: ID!, $take: Int) {
              accessGroup(id: $id) {
                id
              }
              accessGroups(take: $take) {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        Assert.That(result, Does.Contain("query Groups($take: Int)"));
        Assert.That(result, Does.Not.Contain("$id"));
    }

    [Test]
    public void KeepsAVariableSomethingElseStillUses()
    {
        var text =
            """
            query Groups($id: ID!) {
              accessGroup(id: $id) {
                id
              }
              role(id: $id) {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        Assert.That(result, Does.Contain("query Groups($id: ID!)"));
        Assert.That(result, Does.Contain("role(id: $id)"));
    }

    /// <summary>A variable can sit at any depth inside a list or input object literal.</summary>
    [Test]
    public void FindsAVariableNestedInAnArgumentValue()
    {
        var text =
            """
            query Groups($title: String!) {
              accessGroup(where: [{path: "title", value: $title}]) {
                id
              }
              accessGroups(where: [{path: "title", value: $title}]) {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        Assert.That(result, Does.Contain("query Groups($title: String!)"));
    }

    /// <summary>A path can be satisfied by a field the query only reaches through a spread.</summary>
    [Test]
    public void ResolvesThroughAFragmentSpread()
    {
        var text =
            """
            query {
              accessGroups {
                ...Details
              }
            }

            fragment Details on AccessGroup {
              id
              members {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroups", "members"]);

        Assert.That(result, Does.Not.Contain("members"));
        Assert.That(result, Does.Contain("...Details"));
        Assert.That(result, Does.Contain("id"));
    }

    [Test]
    public void ReturnsNullWhenThePathDoesNotResolve()
    {
        var text =
            """
            query {
              accessGroups {
                id
              }
            }
            """;

        Assert.That(FieldRemover.Remove(text, ["somethingElse"]), Is.Null);
        Assert.That(FieldRemover.Remove(text, ["accessGroups", "gone"]), Is.Null);
        Assert.That(FieldRemover.Remove(text, []), Is.Null);
    }

    [Test]
    public void ReturnsNullWhenTheDocumentDoesNotParse() =>
        Assert.That(FieldRemover.Remove("query { accessGroup", ["accessGroup"]), Is.Null);

    /// <summary>The result has to be something the editor can go on validating.</summary>
    [Test]
    public void LeavesAParsableDocument()
    {
        var text =
            """
            query Groups($id: ID!) {
              accessGroup(id: $id) {
                id
              }
              accessGroups {
                id
                members {
                  id
                }
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        Assert.That(result, Is.Not.Null);
        Assert.That(DocumentInfo.Parse(result!).Parses, Is.True);
    }
}
