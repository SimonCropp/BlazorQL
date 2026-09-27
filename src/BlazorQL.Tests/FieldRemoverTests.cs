/// <summary>
/// Removing the field an error points at. The cases that matter are the ones where a naive text
/// delete leaves a document the server will not take: an emptied selection set, an orphaned
/// variable, a name that appears in more than one place.
/// </summary>
public class FieldRemoverTests
{
    [Test]
    public async Task RemovesATopLevelField()
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

        await Assert.That(result).IsEqualTo("""
                query {
                  accessGroups {
                    id
                  }
                }
                """);
    }

    [Test]
    public async Task RemovesANestedField()
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

        await Assert.That(result).IsEqualTo("""
                query {
                  accessGroups {
                    id
                  }
                }
                """);
    }

    /// <summary>
    /// The path carries indices for list elements; the document mentions the selection set once.
    /// </summary>
    [Test]
    public async Task IgnoresListIndicesInThePath()
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

        await Assert.That(result).DoesNotContain("members");
        await Assert.That(result).Contains("accessGroups");
    }

    /// <summary>The path names response keys, so an alias is what a segment matches.</summary>
    [Test]
    public async Task MatchesOnTheAlias()
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

        await Assert.That(result).Contains("first: accessGroup");
        await Assert.That(result).DoesNotContain("second");
    }

    /// <summary>Emptied braces do not parse, so the parent goes as well.</summary>
    [Test]
    public async Task TakesTheParentWhenItWouldBeLeftEmpty()
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

        await Assert.That(result).IsEqualTo("""
                query {
                  other
                }
                """);
    }

    /// <summary>
    /// Nothing would be left to select, so there is no removal that leaves a valid document and the
    /// action reports that rather than producing one.
    /// </summary>
    [Test]
    public async Task RefusesWhenTheOperationWouldBeEmptied()
    {
        var text =
            """
            query {
              accessGroup {
                id
              }
            }
            """;

        await Assert.That(FieldRemover.Remove(text, ["accessGroup"])).IsNull();
        await Assert.That(FieldRemover.Remove(text, ["accessGroup", "id"])).IsNull();
    }

    /// <summary>An unused variable is a validation error, so removing its last use removes it.</summary>
    [Test]
    public async Task DropsAVariableNothingUsesAnyMore()
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

        await Assert.That(result).Contains("query Groups($take: Int)");
        await Assert.That(result).DoesNotContain("$id");
    }

    [Test]
    public async Task KeepsAVariableSomethingElseStillUses()
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

        await Assert.That(result).Contains("query Groups($id: ID!)");
        await Assert.That(result).Contains("role(id: $id)");
    }

    /// <summary>A variable can sit at any depth inside a list or input object literal.</summary>
    [Test]
    public async Task FindsAVariableNestedInAnArgumentValue()
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

        await Assert.That(result).Contains("query Groups($title: String!)");
    }

    /// <summary>A path can be satisfied by a field the query only reaches through a spread.</summary>
    [Test]
    public async Task ResolvesThroughAFragmentSpread()
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

        await Assert.That(result).DoesNotContain("members");
        await Assert.That(result).Contains("...Details");
        await Assert.That(result).Contains("id");
    }

    [Test]
    public async Task ReturnsNullWhenThePathDoesNotResolve()
    {
        var text =
            """
            query {
              accessGroups {
                id
              }
            }
            """;

        await Assert.That(FieldRemover.Remove(text, ["somethingElse"])).IsNull();
        await Assert.That(FieldRemover.Remove(text, ["accessGroups", "gone"])).IsNull();
        await Assert.That(FieldRemover.Remove(text, [])).IsNull();
    }

    [Test]
    public async Task ReturnsNullWhenTheDocumentDoesNotParse() =>
        await Assert.That(FieldRemover.Remove("query { accessGroup", ["accessGroup"])).IsNull();

    /// <summary>The result has to be something the editor can go on validating.</summary>
    [Test]
    public async Task LeavesAParsableDocument()
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

        await Assert.That(result).IsNotNull();
        await Assert.That(DocumentInfo.Parse(result!).Parses).IsTrue();
    }

    /// <summary>
    /// A spread does not consume a path segment, so a fragment cycle is a branch the walk can
    /// re-enter forever. NoFragmentCycles is a validator gap, so such a document does get here.
    /// </summary>
    [Test]
    public async Task ReturnsNullForAPathThatDoesNotResolveThroughASelfSpreadingFragment()
    {
        var text =
            """
            query {
              accessGroups {
                ...F
              }
            }

            fragment F on AccessGroup {
              id
              ...F
            }
            """;

        await Assert.That(FieldRemover.Remove(text, ["accessGroups", "gone"])).IsNull();
    }

    [Test]
    public async Task ResolvesThroughAPairOfFragmentsThatSpreadEachOther()
    {
        var text =
            """
            query {
              accessGroups {
                ...A
              }
            }

            fragment A on AccessGroup {
              id
              ...B
            }

            fragment B on AccessGroup {
              name
              ...A
            }
            """;

        await Assert.That(FieldRemover.Remove(text, ["accessGroups", "gone"])).IsNull();
        await Assert.That(FieldRemover.Remove(text, ["accessGroups", "name"])).IsEqualTo("""
                query {
                  accessGroups {
                    ...A
                  }
                }

                fragment A on AccessGroup {
                  id
                  ...B
                }

                fragment B on AccessGroup {
                  ...A
                }
                """);
    }
}