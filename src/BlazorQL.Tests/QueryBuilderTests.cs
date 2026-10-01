using GraphQLParser.AST;

/// <summary>
/// The query builder's edits, over <see cref="BuilderSchema"/>. Every edit is a splice, so each
/// expectation is the whole document — what changed, and that nothing else did.
/// </summary>
public class QueryBuilderTests
{
    // Per test, not shared: see BuilderSchema.Create.
    readonly SchemaIndex schema = BuilderSchema.Create();

    /// <summary>Steps written as the pane shows them: a field by name, an inline fragment as <c>...Type</c>.</summary>
    static BuilderStep[] Path(params string[] steps) =>
    [
        .. steps.Select(_ => _.StartsWith("...", StringComparison.Ordinal)
            ? BuilderStep.Fragment(_[3..])
            : BuilderStep.Field(_))
    ];

    string? Toggle(string text, params string[] path) =>
        QueryBuilder.ToggleSelection(schema, text, 0, Path(path));

    // ---- Selecting fields ----

    // A composite field goes in with LeafFiller's choice of members, and in the order the schema
    // declares the fields rather than at the end.
    [Test]
    public async Task AddsAFieldInSchemaOrder() =>
        await Assert.That(Toggle(
                """
                query Q {
                  version
                }
                """,
                "viewer"))
            .IsEqualTo(
                """
                query Q {
                  viewer {
                    id
                  }
                  version
                }
                """);

    [Test]
    public async Task AppendsAFieldTheSchemaDeclaresLast() =>
        await Assert.That(Toggle(
                """
                {
                  viewer {
                    id
                  }
                }
                """,
                "version"))
            .IsEqualTo(
                """
                {
                  viewer {
                    id
                  }
                  version
                }
                """);

    [Test]
    public async Task RemovesASelectedField() =>
        await Assert.That(Toggle(
                """
                {
                  viewer {
                    id
                  }
                  version
                }
                """,
                "version"))
            .IsEqualTo(
                """
                {
                  viewer {
                    id
                  }
                }
                """);

    // Required arguments go in with placeholders, so the document validates the moment it changes.
    [Test]
    public async Task AddsTheArgumentsAFieldCannotDoWithout() =>
        await Assert.That(Toggle(
                """
                {
                  version
                }
                """,
                "user"))
            .IsEqualTo(
                """
                {
                  user(id: "") {
                    id
                  }
                  version
                }
                """);

    // A connection has no id of its own, so the default reaches through edges and node to one.
    [Test]
    public async Task ReachesThroughAConnectionForItsDefault() =>
        await Assert.That(Toggle(
                """
                {
                  viewer {
                    id
                  }
                }
                """,
                "viewer",
                "friends"))
            .IsEqualTo(
                """
                {
                  viewer {
                    id
                    friends {
                      edges {
                        node {
                          id
                        }
                      }
                    }
                  }
                }
                """);

    // Unchecking a field's last member leaves the field, which is still chosen: the validator says
    // what it lacks, and running fills it in. Braces around nothing would not parse.
    [Test]
    public async Task LeavesAFieldBareWhenItsLastMemberGoes() =>
        await Assert.That(Toggle(
                """
                {
                  viewer {
                    id
                  }
                }
                """,
                "viewer",
                "id"))
            .IsEqualTo(
                """
                {
                  viewer
                }
                """);

    [Test]
    public async Task GivesABareFieldBracesForItsFirstMember() =>
        await Assert.That(Toggle(
                """
                {
                  viewer
                }
                """,
                "viewer",
                "name"))
            .IsEqualTo(
                """
                {
                  viewer {
                    name
                  }
                }
                """);

    // An operation with nothing selected is no operation; the one beside it closes up to a single
    // blank line as before.
    [Test]
    public async Task RemovesAnOperationWhenItsLastFieldGoes() =>
        await Assert.That(Toggle(
                """
                query A {
                  version
                }

                query B {
                  viewer {
                    id
                  }
                }

                """,
                "version"))
            .IsEqualTo(
                """
                query B {
                  viewer {
                    id
                  }
                }

                """);

    // A field selected twice under two aliases is one checked box, and one click takes both.
    [Test]
    public async Task RemovesEveryAliasOfAField() =>
        await Assert.That(Toggle(
                """
                {
                  first: user(id: "1") {
                    id
                  }
                  second: user(id: "2") {
                    id
                  }
                  version
                }
                """,
                "user"))
            .IsEqualTo(
                """
                {
                  version
                }
                """);

    // A comment just before a closing brace attaches to nothing, so a reprint would lose it. A
    // splice never touches it.
    [Test]
    public async Task KeepsACommentNoNodeOwns() =>
        await Assert.That(Toggle(
                """
                {
                  viewer {
                    id
                  }
                  # the version, for the footer
                }
                """,
                "version"))
            .IsEqualTo(
                """
                {
                  viewer {
                    id
                  }
                  # the version, for the footer
                  version
                }
                """);

    // The comment above a field describes it, so it goes when the field does.
    [Test]
    public async Task TakesAFieldsCommentWithIt() =>
        await Assert.That(Toggle(
                """
                {
                  # who is asking
                  viewer {
                    id
                  }
                  version
                }
                """,
                "viewer"))
            .IsEqualTo(
                """
                {
                  version
                }
                """);

    [Test]
    public async Task WritesIntoASetLaidOutOnOneLine() =>
        await Assert.That(Toggle("{ version }", "viewer"))
            .IsEqualTo("{ viewer { id } version }");

    [Test]
    public async Task RemovesFromASetLaidOutOnOneLine() =>
        await Assert.That(Toggle("{ viewer { id } version }", "viewer"))
            .IsEqualTo("{ version }");

    [Test]
    public async Task FollowsTheIndentationTheDocumentUses() =>
        await Assert.That(Toggle(
                """
                query {
                    version
                }
                """,
                "viewer"))
            .IsEqualTo(
                """
                query {
                    viewer {
                        id
                    }
                    version
                }
                """);

    // Only what an edit touches changes: a hand-laid-out field elsewhere stays as written.
    [Test]
    public async Task LeavesTheRestOfTheDocumentAsWritten()
    {
        var text =
            """
            {
              users(first: 2,   role: ADMIN) { id name }
              version
            }
            """;

        await Assert.That(Toggle(text, "viewer"))
            .IsEqualTo(
                """
                {
                  users(first: 2,   role: ADMIN) { id name }
                  viewer {
                    id
                  }
                  version
                }
                """);
    }

    [Test]
    public async Task ReturnsNullForAPathThatNoLongerResolves() =>
        await Assert.That(Toggle("{ version }", "viewer", "name")).IsNull();

    [Test]
    public async Task ReturnsNullForTextThatDoesNotParse() =>
        await Assert.That(Toggle("{ version", "viewer")).IsNull();

    [Test]
    public async Task ReturnsNullForAFieldTheSchemaDoesNotHave() =>
        await Assert.That(Toggle("{ version }", "missing")).IsNull();

    // ---- Abstract types ----

    // A union has no fields of its own, so a new union field selects __typename, and its members are
    // reached through inline fragments.
    [Test]
    public async Task SelectsAUnionMemberThroughAnInlineFragment()
    {
        var added = Toggle(
            """
            {
              version
            }
            """,
            "search")!;

        await Assert.That(added).IsEqualTo(
            """
            {
              search(term: "") {
                __typename
              }
              version
            }
            """);

        await Assert.That(Toggle(added, "search", "...User")).IsEqualTo(
            """
            {
              search(term: "") {
                __typename
                ... on User {
                  id
                }
              }
              version
            }
            """);
    }

    // An inline fragment emptied of its last member goes, rather than leaving braces around nothing.
    [Test]
    public async Task RemovesAnInlineFragmentWithItsLastMember() =>
        await Assert.That(Toggle(
                """
                {
                  search(term: "x") {
                    __typename
                    ... on User {
                      id
                    }
                  }
                }
                """,
                "search",
                "...User",
                "id"))
            .IsEqualTo(
                """
                {
                  search(term: "x") {
                    __typename
                  }
                }
                """);

    [Test]
    public async Task SelectsAnInterfaceFieldDirectly() =>
        await Assert.That(Toggle(
                """
                {
                  node(id: "1") {
                    __typename
                  }
                }
                """,
                "node",
                "id"))
            .IsEqualTo(
                """
                {
                  node(id: "1") {
                    id
                    __typename
                  }
                }
                """);

    // ---- Fragments ----

    [Test]
    public async Task SpreadsAFragmentAndTakesItOutAgain()
    {
        var text =
            """
            {
              viewer {
                id
              }
            }

            fragment UserFields on User {
              name
            }
            """;

        var spread = QueryBuilder.ToggleSpread(schema, text, 0, Path("viewer"), "UserFields");
        await Assert.That(spread).IsEqualTo(
            """
            {
              viewer {
                id
                ...UserFields
              }
            }

            fragment UserFields on User {
              name
            }
            """);

        await Assert.That(QueryBuilder.ToggleSpread(schema, spread!, 0, Path("viewer"), "UserFields")).IsEqualTo(text);
    }

    // A fragment is a selection set on its type condition, edited the same way as an operation.
    [Test]
    public async Task EditsAFragmentDefinition() =>
        await Assert.That(QueryBuilder.ToggleSelection(
                schema,
                """
                {
                  viewer {
                    ...UserFields
                  }
                }

                fragment UserFields on User {
                  name
                }
                """,
                1,
                Path("id")))
            .IsEqualTo(
                """
                {
                  viewer {
                    ...UserFields
                  }
                }

                fragment UserFields on User {
                  id
                  name
                }
                """);

    // ---- Arguments ----

    string? ToggleArgument(string text, string[] path, params string[] input) =>
        QueryBuilder.ToggleArgument(schema, text, 0, Path(path), input);

    string? SetArgument(string text, string[] path, string[] input, GraphQLValue value) =>
        QueryBuilder.SetArgument(schema, text, 0, Path(path), input, value);

    [Test]
    public async Task SwitchesAnArgumentOnWithAPlaceholder() =>
        await Assert.That(ToggleArgument("{ users { id } }", ["users"], "role"))
            .IsEqualTo("{ users(role: ADMIN) { id } }");

    // Arguments go in the order the schema declares them, wherever the click came from.
    [Test]
    public async Task PutsAnArgumentInSchemaOrder() =>
        await Assert.That(ToggleArgument("{ users(role: ADMIN) { id } }", ["users"], "first"))
            .IsEqualTo("{ users(first: 0, role: ADMIN) { id } }");

    // The last argument takes its parentheses with it: empty ones do not parse.
    [Test]
    public async Task TakesTheParenthesesWithTheLastArgument() =>
        await Assert.That(ToggleArgument("{ users(role: ADMIN) { id } }", ["users"], "role"))
            .IsEqualTo("{ users { id } }");

    [Test]
    public async Task TakesAnArgumentWithTheSeparatorAfterIt() =>
        await Assert.That(ToggleArgument("{ users(first: 1, role: ADMIN) { id } }", ["users"], "first"))
            .IsEqualTo("{ users(role: ADMIN) { id } }");

    [Test]
    public async Task TakesTheLastArgumentWithTheSeparatorBeforeIt() =>
        await Assert.That(ToggleArgument("{ users(first: 1, role: ADMIN) { id } }", ["users"], "role"))
            .IsEqualTo("{ users(first: 1) { id } }");

    [Test]
    public async Task KeepsArgumentsOneToALineWhereTheyWereWritten() =>
        await Assert.That(ToggleArgument(
                """
                {
                  users(
                    first: 1
                    role: ADMIN
                  ) {
                    id
                  }
                }
                """,
                ["users"],
                "filter"))
            .IsEqualTo(
                """
                {
                  users(
                    first: 1
                    filter: {required: ""}
                    role: ADMIN
                  ) {
                    id
                  }
                }
                """);

    // An input object's placeholder carries its required members, so it validates as it stands.
    [Test]
    public async Task GivesAnInputObjectItsRequiredMembers() =>
        await Assert.That(ToggleArgument("{ users { id } }", ["users"], "filter"))
            .IsEqualTo("""{ users(filter: {required: ""}) { id } }""");

    [Test]
    public async Task SwitchesAnInputFieldOn() =>
        await Assert.That(ToggleArgument("""{ users(filter: {required: ""}) { id } }""", ["users"], "filter", "age"))
            .IsEqualTo("""{ users(filter: {age: {min: 0}, required: ""}) { id } }""");

    // The last member of an object leaves the object empty, which parses, rather than taking the
    // argument with it.
    [Test]
    public async Task LeavesAnObjectEmptyWhenItsLastFieldGoes() =>
        await Assert.That(ToggleArgument("""{ users(filter: {required: ""}) { id } }""", ["users"], "filter", "required"))
            .IsEqualTo("{ users(filter: {}) { id } }");

    [Test]
    public async Task SetsAValue() =>
        await Assert.That(SetArgument("{ users(first: 1) { id } }", ["users"], ["first"], new GraphQLIntValue(25)))
            .IsEqualTo("{ users(first: 25) { id } }");

    [Test]
    public async Task EscapesAStringValue() =>
        await Assert.That(SetArgument("""{ user(id: "") { id } }""", ["user"], ["id"], new GraphQLStringValue("a\"b\\c")))
            .IsEqualTo("""{ user(id: "a\"b\\c") { id } }""");

    [Test]
    public async Task SetsAValueThatWasNotThere() =>
        await Assert.That(SetArgument("{ users { id } }", ["users"], ["role"], new GraphQLEnumValue(new("EDITOR"))))
            .IsEqualTo("{ users(role: EDITOR) { id } }");

    [Test]
    public async Task SetsAValueInsideAnInputObject() =>
        await Assert.That(SetArgument("""{ users(filter: {required: ""}) { id } }""", ["users"], ["filter", "required"], new GraphQLStringValue("yes")))
            .IsEqualTo("""{ users(filter: {required: "yes"}) { id } }""");

    [Test]
    public async Task ReportsNoChangeForTheValueAlreadyWritten() =>
        await Assert.That(SetArgument("{ users(first: 1) { id } }", ["users"], ["first"], new GraphQLIntValue(1)))
            .IsNull();

    // ---- Literals ----

    [Test]
    [Arguments("Int", "12", "12")]
    [Arguments("Int", " -3 ", "-3")]
    [Arguments("Int", "1.5", null)]
    [Arguments("Int", "99999999999", null)]
    [Arguments("Int", "012", null)]
    [Arguments("Float", "1.5e3", "1.5e3")]
    [Arguments("Float", "2", "2")]
    [Arguments("Float", "two", null)]
    [Arguments("Boolean", "true", "true")]
    [Arguments("Boolean", "yes", null)]
    [Arguments("Role", "EDITOR", "EDITOR")]
    [Arguments("Role", "NOPE", null)]
    [Arguments("ID", "42", "\"42\"")]
    [Arguments("String", "say \"hi\"", "\"say \\\"hi\\\"\"")]
    public async Task ReadsAnInputAsALiteral(string type, string input, string? expected)
    {
        var literal = QueryBuilder.Literal(schema, new() {Kind = "SCALAR", Name = type}, input);

        await Assert.That(literal is null ? null : QueryBuilder.Print(literal)).IsEqualTo(expected);
    }

    // ---- Variables ----

    // The literal becomes the variable's default, so the operation asks what it asked before.
    [Test]
    public async Task TurnsALiteralIntoAVariableAndBack()
    {
        var text =
            """
            query Q {
              user(id: "7") {
                id
              }
            }
            """;

        var variable = QueryBuilder.ToggleVariable(schema, text, 0, Path("user"), "id");
        await Assert.That(variable).IsEqualTo(
            """
            query Q($id: ID! = "7") {
              user(id: $id) {
                id
              }
            }
            """);

        await Assert.That(QueryBuilder.ToggleVariable(schema, variable!, 0, Path("user"), "id")).IsEqualTo(text);
    }

    // The shorthand query has no keyword to hang a variable list on, so it takes one.
    [Test]
    public async Task GivesTheShorthandQueryAKeywordForItsVariable() =>
        await Assert.That(QueryBuilder.ToggleVariable(schema, """{ user(id: "7") { id } }""", 0, Path("user"), "id"))
            .IsEqualTo("""query ($id: ID! = "7") { user(id: $id) { id } }""");

    [Test]
    public async Task NamesAVariableAfterItsFieldWhenTheArgumentsNameIsTaken() =>
        await Assert.That(QueryBuilder.ToggleVariable(schema, """query Q($id: ID) { node(id: $id) { id } user(id: "7") { id } }""", 0, Path("user"), "id"))
            .IsEqualTo("""query Q($id: ID, $userId: ID! = "7") { node(id: $id) { id } user(id: $userId) { id } }""");

    // Removing the only reader of a variable takes the declaration too: an unused variable is a
    // validation error of its own.
    [Test]
    public async Task TakesOutTheDeclarationARemovedFieldWasTheLastToRead() =>
        await Assert.That(Toggle(
                """
                query Q($id: ID!) {
                  user(id: $id) {
                    id
                  }
                  version
                }
                """,
                "user"))
            .IsEqualTo(
                """
                query Q {
                  version
                }
                """);

    // A variable declared ahead of the field that will read it is work in progress, not an orphan.
    [Test]
    public async Task LeavesAVariableNothingHasReadYet() =>
        await Assert.That(Toggle(
                """
                query Q($later: Int) {
                  version
                }
                """,
                "viewer"))
            .IsEqualTo(
                """
                query Q($later: Int) {
                  viewer {
                    id
                  }
                  version
                }
                """);

    [Test]
    public async Task HasNoVariablesForAFragment() =>
        await Assert.That(QueryBuilder.ToggleVariable(schema, """fragment F on Query { user(id: "1") { id } }""", 0, Path("user"), "id"))
            .IsNull();

    // ---- Variables document ----

    string? VariablesAfter(string before, string after, string variables) =>
        QueryBuilder.VariablesEdit(schema, before, after, 0)?.Invoke(variables);

    // The literal the argument held is the value the variable goes in with.
    [Test]
    public async Task AVariableTheEditDeclaresGoesIntoTheVariables()
    {
        var before = """query Q { user(id: "7") { id } }""";
        var after = QueryBuilder.ToggleVariable(schema, before, 0, Path("user"), "id")!;
        await Assert.That(VariablesAfter(before, after, "")).IsEqualTo(
            """
            {
              "id": "7"
            }
            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task AVariableTheEditDropsComesOutOfTheVariables()
    {
        var before = """query Q($id: ID! = "7") { user(id: $id) { id } }""";
        var after = QueryBuilder.ToggleVariable(schema, before, 0, Path("user"), "id")!;
        await Assert.That(VariablesAfter(before, after, """{"id": "9", "other": 1}"""))
            .IsEqualTo(
                """
                {
                  "other": 1
                }
                """.ReplaceLineEndings("\n"));
    }

    // A value somebody typed is kept, and a document that is not JSON is left alone.
    [Test]
    public async Task TheVariablesKeepAValueAlreadyThere()
    {
        var before = """query Q { user(id: "7") { id } }""";
        var after = QueryBuilder.ToggleVariable(schema, before, 0, Path("user"), "id")!;
        await Assert.That(VariablesAfter(before, after, """{"id": "9"}""")).Contains("\"9\"");
        await Assert.That(VariablesAfter(before, after, "{ not json")).IsNull();
    }

    [Test]
    public async Task AnEditThatDeclaresNothingLeavesTheVariablesAlone() =>
        await Assert.That(QueryBuilder.VariablesEdit(schema, "query Q { version }", "query Q { version user(id: \"1\") { id } }", 0)).IsNull();

    // ---- Operations ----

    // The welcome text is comments only, which parses as a document with nothing in it. The new
    // operation goes after it, and the comments stay as written.
    [Test]
    public async Task AddsAnOperationAfterTheWelcomeText() =>
        await Assert.That(QueryBuilder.AddOperation(schema, "# Welcome\n#\n# Type a query.\n\n", OperationType.Query, "version"))
            .IsEqualTo(
                """
                # Welcome
                #
                # Type a query.

                query MyQuery {
                  version
                }

                """);

    [Test]
    public async Task AddsAnOperationToABlankDocument() =>
        await Assert.That(QueryBuilder.AddOperation(schema, "", OperationType.Mutation, "rename"))
            .IsEqualTo(
                """
                mutation MyMutation {
                  rename(id: "", name: "") {
                    id
                  }
                }

                """);

    [Test]
    public async Task NumbersANewOperationPastTheNamesTaken() =>
        await Assert.That(QueryBuilder.AddOperation(schema, "query MyQuery { version }", OperationType.Query, "viewer"))
            .IsEqualTo(
                """
                query MyQuery { version }

                query MyQuery2 {
                  viewer {
                    id
                  }
                }

                """);

    [Test]
    public async Task AddsNothingForARootTheSchemaLacks() =>
        await Assert.That(QueryBuilder.AddOperation(schema, "", OperationType.Subscription, "version")).IsNull();

    [Test]
    [Arguments("query Q { version }", "R", "query R { version }")]
    [Arguments("query Q { version }", "", "query { version }")]
    [Arguments("query Q($a: Int) { version }", " ", "query ($a: Int) { version }")]
    [Arguments("query { version }", "R", "query R { version }")]
    [Arguments("{ version }", "R", "query R { version }")]
    [Arguments("mutation($a: Int) { rename(id: \"\", name: \"\") { id } }", "M", "mutation M($a: Int) { rename(id: \"\", name: \"\") { id } }")]
    public async Task RenamesAnOperation(string text, string name, string expected) =>
        await Assert.That(QueryBuilder.RenameOperation(text, 0, name)).IsEqualTo(expected);

    // A half-typed name leaves the document alone rather than breaking it.
    [Test]
    [Arguments("2fast")]
    [Arguments("my-query")]
    [Arguments("Q")]
    public async Task IgnoresANameThatChangesNothingOrIsNotOne(string name) =>
        await Assert.That(QueryBuilder.RenameOperation("query Q { version }", 0, name)).IsNull();

    [Test]
    public async Task RemovesAnOperationFromBetweenTwoOthers() =>
        await Assert.That(QueryBuilder.RemoveOperation(
                """
                query A { version }

                query B { version }

                query C { version }

                """,
                1))
            .IsEqualTo(
                """
                query A { version }

                query C { version }

                """);

    [Test]
    public async Task RemovesTheLastOperationWithTheBlankLineBeforeIt() =>
        await Assert.That(QueryBuilder.RemoveOperation(
                """
                # notes
                query A { version }

                query B { version }
                """,
                1))
            .IsEqualTo(
                """
                # notes
                query A { version }

                """);

    [Test]
    public async Task KeepsTheCommentsAboveARemovedOperation() =>
        await Assert.That(QueryBuilder.RemoveOperation("# Welcome\n\nquery A { version }\n", 0))
            .IsEqualTo("# Welcome\n");

    // ---- What it builds is valid ----

    // Everything the builder can put in validates: each root field, each with a placeholder for every
    // argument it takes, and each input object argument with every one of its fields filled in.
    [Test]
    public async Task EverythingItAddsValidates()
    {
        var validator = new SchemaValidator(schema);
        foreach (var field in schema.Find(schema.QueryTypeName)!.Fields!)
        {
            var text = QueryBuilder.AddOperation(schema, "", OperationType.Query, field.Name)!;
            text = FillArguments(text, [field.Name], field);
            await AssertValid(validator, text);
        }

        // And a level down, where every member of a type comes in beside the others.
        var nested = QueryBuilder.AddOperation(schema, "", OperationType.Query, "viewer")!;
        foreach (var field in schema.Find("User")!.Fields!)
        {
            if (QueryBuilder.Field(ViewerSet(nested), field.Name) is null)
            {
                nested = Toggle(nested, "viewer", field.Name)!;
            }

            nested = FillArguments(nested, ["viewer", field.Name], field);
        }

        await AssertValid(validator, nested);
    }

    static GraphQLSelectionSet? ViewerSet(string text) =>
        QueryBuilder.Field(DocumentInfo.Parse(text).OperationNode(null)!.SelectionSet, "viewer")?.SelectionSet;

    // Set rather than toggled: a required argument is already there, and a toggle would take it out.
    string FillArguments(string text, string[] path, IntrospectionField field)
    {
        foreach (var argument in field.Args)
        {
            text = QueryBuilder.SetArgument(schema, text, 0, Path(path), [argument.Name], QueryBuilder.DefaultValue(schema, argument.Type)) ?? text;
            if (schema.Find(argument.Type.Unwrap().Name) is not {Kind: "INPUT_OBJECT"} input)
            {
                continue;
            }

            foreach (var inputField in input.InputFields!)
            {
                text = QueryBuilder.SetArgument(schema, text, 0, Path(path), [argument.Name, inputField.Name], QueryBuilder.DefaultValue(schema, inputField.Type)) ?? text;
            }
        }

        return text;
    }

    static async Task AssertValid(SchemaValidator validator, string text)
    {
        var errors = validator.Validate(DocumentInfo.Parse(text))
            .Where(_ => _.IsError)
            .Select(_ => _.Message)
            .ToList();

        await Assert.That(errors).IsEmpty().Because(text);
    }
}
