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

    /// <summary>
    /// The field is all an inline fragment selects, so it is the fragment that goes — not the field
    /// the fragment sits in, which still has its other selections.
    /// </summary>
    [Test]
    public async Task TakesAnInlineFragmentItWouldLeaveEmptyRatherThanItsField()
    {
        var text =
            """
            query Node($id: ID!) {
              node(id: $id) {
                id
                ... on Repository {
                  broken
                }
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["node", "broken"]);

        await Assert.That(result).IsEqualTo("""
                query Node($id: ID!) {
                  node(id: $id) {
                    id
                  }
                }
                """);
    }

    [Test]
    public async Task ClimbsPastAnInlineFragmentWhenItsFieldWouldBeLeftEmptyToo()
    {
        var text =
            """
            query {
              node {
                ... on Repository {
                  broken
                }
              }
              other
            }
            """;

        var result = FieldRemover.Remove(text, ["node", "broken"]);

        await Assert.That(result).IsEqualTo("""
                query {
                  other
                }
                """);
    }

    /// <summary>
    /// A fragment with nothing else in it would be left empty, so the spread on the error's path goes,
    /// and the definition with it once nothing spreads it — an unused fragment is a validation
    /// error of its own. A variable only the fragment used goes too.
    /// </summary>
    [Test]
    public async Task TakesASpreadItWouldLeaveEmptyAndTheFragmentNothingElseSpreads()
    {
        var text =
            """
            query Node($id: ID!, $first: Int) {
              node(id: $id) {
                id
                ...Repo
              }
            }

            fragment Repo on Repository {
              broken(first: $first)
            }
            """;

        var result = FieldRemover.Remove(text, ["node", "broken"]);

        await Assert.That(result).IsEqualTo("""
                query Node($id: ID!) {
                  node(id: $id) {
                    id
                  }
                }
                """);
    }

    /// <summary>
    /// The definitions either side of a removed one keep the single blank line between them, and the
    /// document still ends as it did.
    /// </summary>
    [Test]
    public async Task ARemovedFragmentTakesItsBlankLineWithIt()
    {
        var text =
            """
            query {
              node {
                id
                ...Repo
              }
            }

            fragment Repo on Repository {
              broken
            }

            fragment Kept on Node {
              id
            }

            """;

        var result = FieldRemover.Remove(text, ["node", "broken"]);

        await Assert.That(result).IsEqualTo("""
                query {
                  node {
                    id
                  }
                }

                fragment Kept on Node {
                  id
                }

                """);
    }

    [Test]
    public async Task KeepsAFragmentAnotherSpreadStillUses()
    {
        var text =
            """
            query {
              first: node {
                id
                ...Repo
              }
              second: node {
                id
                ...Repo
              }
            }

            fragment Repo on Repository {
              broken
            }
            """;

        var result = FieldRemover.Remove(text, ["first", "broken"]);

        await Assert.That(result).IsEqualTo("""
                query {
                  first: node {
                    id
                  }
                  second: node {
                    id
                    ...Repo
                  }
                }

                fragment Repo on Repository {
                  broken
                }
                """);
    }

    /// <summary>A variable a removed inline fragment's own directive read goes with it.</summary>
    [Test]
    public async Task DropsAVariableOnlyTheRemovedFragmentsDirectiveRead()
    {
        var text =
            """
            query Node($id: ID!, $withRepo: Boolean!) {
              node(id: $id) {
                id
                ... on Repository @include(if: $withRepo) {
                  broken
                }
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["node", "broken"]);

        await Assert.That(result).Contains("query Node($id: ID!) {");
        await Assert.That(result).DoesNotContain("withRepo");
    }

    /// <summary>
    /// Several operations can select the same path, and the error came from the one that ran. The
    /// others are not what the response was about.
    /// </summary>
    [Test]
    public async Task RemovesFromTheOperationThatRan()
    {
        var text =
            """
            query First {
              user {
                broken
                name
              }
            }

            query Second {
              user {
                broken
                name
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["user", "broken"], "Second");

        await Assert.That(result).IsEqualTo("""
                query First {
                  user {
                    broken
                    name
                  }
                }

                query Second {
                  user {
                    name
                  }
                }
                """);
    }

    [Test]
    public async Task ReturnsNullWhenThePathOnlyResolvesInAnOperationThatDidNotRun()
    {
        var text =
            """
            query First {
              user {
                broken
                name
              }
            }

            query Second {
              user {
                name
              }
            }
            """;

        await Assert.That(FieldRemover.Remove(text, ["user", "broken"], "Second")).IsNull();
    }

    /// <summary>
    /// A run with no operation name runs the anonymous operation, so with several in the document
    /// that is the one the response is about.
    /// </summary>
    [Test]
    public async Task WithoutANameRemovesFromTheAnonymousOperation()
    {
        var text =
            """
            query Named {
              user {
                broken
                name
              }
            }

            {
              user {
                broken
                name
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["user", "broken"]);

        await Assert.That(result).StartsWith("""
                query Named {
                  user {
                    broken
                """);
        await Assert.That(result).EndsWith("""
                {
                  user {
                    name
                  }
                }
                """);
    }

    /// <summary>
    /// A document with one operation can only have run that one, whatever name the tab last recorded
    /// — the operation may have been renamed since.
    /// </summary>
    [Test]
    public async Task ASingleOperationIsTheOneThatRanWhateverTheName()
    {
        var text =
            """
            query Renamed {
              user {
                broken
                name
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["user", "broken"], "Original");

        await Assert.That(result).DoesNotContain("broken");
    }

    /// <summary>
    /// Variables belong to an operation, so whether one is still used is a question about that
    /// operation alone: another operation reading a variable of the same name does not keep it.
    /// </summary>
    [Test]
    public async Task DropsAVariableItsOwnOperationNoLongerUsesThoughAnotherDoes()
    {
        var text =
            """
            query First($id: ID!) {
              user(id: $id) {
                name
              }
              other
            }

            query Second($id: ID!) {
              user(id: $id) {
                name
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["user"], "First");

        await Assert.That(result).IsEqualTo("""
                query First {
                  other
                }

                query Second($id: ID!) {
                  user(id: $id) {
                    name
                  }
                }
                """);
    }

    /// <summary>A directive on the operation itself reads variables too.</summary>
    [Test]
    public async Task KeepsAVariableTheOperationsOwnDirectiveReads()
    {
        var text =
            """
            query Groups($ttl: Int!) @cached(ttl: $ttl) {
              accessGroup(ttl: $ttl) {
                id
              }
              accessGroups {
                id
              }
            }
            """;

        var result = FieldRemover.Remove(text, ["accessGroup"]);

        await Assert.That(result).IsEqualTo("""
                query Groups($ttl: Int!) @cached(ttl: $ttl) {
                  accessGroups {
                    id
                  }
                }
                """);
    }

    /// <summary>
    /// Only what the removal orphaned is cleaned up. A variable that was already unused — in this
    /// operation or another — is the user's to deal with, perhaps half way through an edit.
    /// </summary>
    [Test]
    public async Task LeavesAVariableThatWasAlreadyUnused()
    {
        var text =
            """
            query First($unused: Int, $id: ID!) {
              user(id: $id) {
                name
              }
              other
            }

            query Second($alsoUnused: Int) {
              other
            }
            """;

        var result = FieldRemover.Remove(text, ["user"], "First");

        await Assert.That(result).IsEqualTo("""
                query First($unused: Int) {
                  other
                }

                query Second($alsoUnused: Int) {
                  other
                }
                """);
    }

    /// <summary>Two fragments of one name is a validation error, but a document that parses.</summary>
    [Test]
    public async Task ToleratesTwoFragmentsOfOneName()
    {
        var text =
            """
            query {
              user {
                ...F
                name
              }
            }

            fragment F on User {
              broken
              id
            }

            fragment F on User {
              id
            }
            """;

        var result = FieldRemover.Remove(text, ["user", "broken"]);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).DoesNotContain("broken");
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