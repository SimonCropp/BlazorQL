using BlazorQL.Sample;
using GraphQL.Validation.Errors;
using GraphQLParser;
using GraphQLParser.AST;

/// <summary>
/// Differential test: every document runs through both <see cref="SchemaValidator"/> and
/// GraphQL.NET's <see cref="DocumentValidator"/>, over one schema both sides derive from the same
/// <see cref="SampleSchema"/> instance — GraphQL.NET directly, BlazorQL through the introspection
/// response the IDE would actually receive. Two fixtures could drift; one cannot.
/// </summary>
/// <remarks>
/// GraphQL.NET is a test-only dependency. It is what the shipped validator is measured against,
/// never what it is built on: the RCL could carry neither its size nor its trimming behaviour, and
/// it has no introspection-to-schema path, which is the only schema an IDE ever has.
/// <para>
/// The comparison is by which rule fired, not by message text — the two implementations word
/// things differently on purpose (BlazorQL follows graphql-js, because that is what GraphiQL
/// shows). Messages still go into the snapshot, so wording drift is reviewable.
/// </para>
/// </remarks>
[TestFixture]
public class ValidatorParityTests
{
    /// <summary>
    /// Rules GraphQL.NET enforces and <see cref="SchemaValidator"/> does not. A document whose
    /// every GraphQL.NET error falls in here is expected to pass BlazorQL's validation, and the
    /// test asserts that it does — so porting a rule fails this test until the entry is removed.
    /// That is the point: the gap is a list someone has to shorten deliberately, not something
    /// that quietly widens.
    /// </summary>
    /// <remarks>
    /// Keyed by error type rather than by the spec number the error carries, because the numbers
    /// are not unique: ArgumentsOfCorrectType and DefaultValuesOfCorrectType are both 5.6.1, and
    /// BlazorQL implements one of them. The value is where the rule lives in <see cref="upstream"/>,
    /// so closing a gap starts by opening one file.
    /// </remarks>
    static readonly Dictionary<string, string> knownGaps = new()
    {
        [nameof(DefaultValuesOfCorrectTypeError)] = "src/GraphQL/Validation/Rules/DefaultValuesOfCorrectType.cs",
        [nameof(NoFragmentCyclesError)] = "src/GraphQL/Validation/Rules/NoFragmentCycles.cs",
        [nameof(OverlappingFieldsCanBeMergedError)] = "src/GraphQL/Validation/Rules/OverlappingFieldsCanBeMerged.cs",
        [nameof(PossibleFragmentSpreadsError)] = "src/GraphQL/Validation/Rules/PossibleFragmentSpreads.cs",
        [nameof(SingleRootFieldSubscriptionsError)] = "src/GraphQL/Validation/Rules/SingleRootFieldSubscriptions.cs",
        [nameof(UniqueArgumentNamesError)] = "src/GraphQL/Validation/Rules/UniqueArgumentNames.cs",
        [nameof(UniqueFragmentNamesError)] = "src/GraphQL/Validation/Rules/UniqueFragmentNames.cs",
        [nameof(UniqueInputFieldNamesError)] = "src/GraphQL/Validation/Rules/UniqueInputFieldNames.cs",
        [nameof(UniqueDirectivesPerLocationError)] = "src/GraphQL/Validation/Rules/5.7 - Directives/3. UniqueDirectivesPerLocation.cs",
        [nameof(UniqueVariableNamesError)] = "src/GraphQL/Validation/Rules/5.8 - Variables/1. UniqueVariableNames.cs",

        // Half implemented, and the half that is missing is what the entry records: SchemaValidator's
        // CheckDirectives knows which directives exist and type-checks their arguments, but never
        // asks whether a directive is allowed where it was written.
        [nameof(DirectivesInAllowedLocationsError)] = "src/GraphQL/Validation/Rules/5.7 - Directives/1+2. KnownDirectivesInAllowedLocations.cs"
    };

    /// <summary>
    /// The reference implementation. Paths in <see cref="knownGaps"/> are relative to the repository
    /// root, so appending one to <c>{Upstream}/blob/master/</c> opens the rule.
    /// </summary>
    const string upstream = "https://github.com/graphql-dotnet/graphql-dotnet";

    /// <summary>
    /// Corpus entries whose divergence is understood and accepted, with the reason. Kept separate
    /// from <see cref="knownGaps"/> because these are not missing rules — they are consequences of
    /// one, or deliberate differences in how far a cascading error is followed.
    /// </summary>
    static readonly Dictionary<string, string> knownDivergences = new()
    {
        ["nullable-variable-null-default-in-non-null"] =
            "BlazorQL is right and the reference is wrong here. Spec 5.8.5 makes " +
            "hasNonNullVariableDefaultValue true only for a default that exists AND is not the " +
            "value null, and graphql-js checks both halves. GraphQL.NET quotes that sentence in a " +
            "comment and then tests only DefaultValue != null, so it accepts $v: Boolean = null in " +
            "a Boolean! position. Reported upstream is the fix; matching it is not.",

        ["duplicate-fragment-name"] =
            "Without UniqueFragmentNames, BlazorQL keeps the first definition of a duplicated " +
            "fragment and treats the name as spread, so it does not go on to report the second " +
            "as unused the way GraphQL.NET does. Closing the UniqueFragmentNames gap subsumes this."
    };

    /// <summary>
    /// Documents to run both validators over. Nothing here says what the answer is — the two
    /// validators do that. Add a case whenever a rule changes; the shape of the divergence lands
    /// in the snapshot.
    /// </summary>
    static IEnumerable<TestCaseData> Corpus()
    {
        // Valid, and exercising the shapes the rules key off: a union spread, arguments of every
        // kind, variables, a fragment, a directive.
        yield return Case("valid-full",
            """
            query Full($name: String, $skip: Boolean = false) {
              test {
                isTest
                hasArgs(string: $name, int: 1, enum: RED, listString: ["a"], object: {string: "s"})
                union { ... on First { name } }
              }
              ...OnTest @skip(if: $skip)
            }
            fragment OnTest on Test {
              image
            }
            """);

        yield return Case("valid-anonymous", "{ test { isTest } }");

        // Spec 5.8.5's two escapes from strict nullability, and the case neither covers.
        yield return Case("nullable-variable-with-default-in-non-null", "query Q($skip: Boolean = false) { test { isTest @skip(if: $skip) } }");
        yield return Case("nullable-variable-null-default-in-non-null", "query Q($skip: Boolean = null) { test { isTest @skip(if: $skip) } }");
        yield return Case("nullable-variable-no-default-in-non-null", "query Q($skip: Boolean) { test { isTest @skip(if: $skip) } }");

        // Rules BlazorQL owns. Both sides should flag every one of these.
        yield return Case("unknown-field", "{ test { noSuchField } }");
        yield return Case("unknown-argument", "{ test { hasArgs(noSuchArg: 1) } }");
        yield return Case("unknown-type-in-fragment", "{ ...F } fragment F on NoSuchType { image }");
        yield return Case("unknown-fragment", "{ test { ...NoSuchFragment } }");
        yield return Case("unused-fragment", "{ test { isTest } } fragment Unused on Test { image }");
        yield return Case("undefined-variable", "{ test { hasArgs(int: $nope) } }");
        yield return Case("unused-variable", "query Q($unused: Int) { test { isTest } }");
        yield return Case("scalar-with-selection", "{ test { isTest { deeper } } }");
        yield return Case("composite-without-selection", "{ test }");
        yield return Case("duplicate-operation-name", "query Q { test { isTest } } query Q { test { isTest } }");
        yield return Case("anonymous-alongside-named", "{ test { isTest } } query Named { test { isTest } }");
        yield return Case("unknown-directive", "{ test { isTest @noSuchDirective } }");
        yield return Case("wrong-argument-type", """{ test { hasArgs(int: "not an int") } }""");
        yield return Case("bad-enum-value", "{ test { hasArgs(enum: NOT_A_COLOR) } }");
        yield return Case("variable-of-output-type", "query Q($v: Test) { test { isTest } }");
        yield return Case("fragment-on-scalar", "{ test { ...F } } fragment F on String { image }");
        yield return Case("input-object-subset", "{ test { hasArgs(object: {int: 1}) } }");
        yield return Case("variable-type-mismatch", "query Q($v: String) { test { hasArgs(int: $v) } }");

        // A variable is in scope for every fragment the operation reaches, however deeply, and for
        // no operation that does not reach it. The Relay-shaped document is the first case; the
        // second is what makes the scope per operation rather than whatever was walked last.
        yield return Case("variable-used-only-in-fragment",
            "query Q($name: String) { test { ...F } } fragment F on Test { hasArgs(string: $name) }");
        yield return Case("variable-used-in-a-nested-fragment",
            "query Q($name: String) { test { ...F } } fragment F on Test { ...G } fragment G on Test { hasArgs(string: $name) }");
        yield return Case("variable-in-fragment-with-two-operations",
            "query A($a: String) { test { ...F } } query B($b: String) { test { hasArgs(string: $b) } } fragment F on Test { hasArgs(string: $a) }");
        yield return Case("variable-of-the-wrong-type-in-a-fragment",
            "query Q($v: String) { test { ...F } } fragment F on Test { hasArgs(int: $v) }");
        yield return Case("undefined-variable-in-a-fragment",
            "query Q { test { ...F } } fragment F on Test { hasArgs(string: $nope) }");
        yield return Case("variable-in-an-unused-fragment",
            "query Q { test { isTest } } fragment F on Test { hasArgs(string: $nope) }");

        // Rules only GraphQL.NET has. These must land entirely in knownGaps, and BlazorQL must
        // report nothing — the assertion that keeps the gap list honest in both directions.
        yield return Case("fragment-cycle", "{ test { ...A } } fragment A on Test { ...B } fragment B on Test { ...A }");
        yield return Case("duplicate-argument-name", """{ test { hasArgs(string: "a", string: "b") } }""");
        yield return Case("duplicate-variable-name", "query Q($v: Int, $v: Int) { test { hasArgs(int: $v) } }");
        yield return Case("impossible-fragment-spread", "{ test { union { ...F } } } fragment F on Greeting { text }");
        // @skip is FIELD | FRAGMENT_SPREAD | INLINE_FRAGMENT, never an operation.
        yield return Case("directive-in-wrong-location", "query Q @skip(if: true) { test { isTest } }");
        yield return Case("duplicate-fragment-name", "{ test { ...F } } fragment F on Test { image } fragment F on Test { isTest }");

        static TestCaseData Case(string name, string query) =>
            new TestCaseData(name, query).SetName($"{{m}}({name})");
    }

    [TestCaseSource(nameof(Corpus))]
    public async Task Parity(string name, string query)
    {
        var reference = await Reference(query);
        var mine = Mine(query);

        var gaps = reference.Where(_ => knownGaps.ContainsKey(_.Rule)).ToList();
        var owned = reference.Where(_ => !knownGaps.ContainsKey(_.Rule)).ToList();

        if (!knownDivergences.ContainsKey(name))
        {
            if (owned.Count > 0)
            {
                Assert.That(
                    mine,
                    Is.Not.Empty,
                    $"GraphQL.NET reported {Describe(owned)}, which BlazorQL claims to implement, but BlazorQL reported nothing.");
            }
            else if (gaps.Count > 0)
            {
                Assert.That(
                    mine,
                    Is.Empty,
                    $"Every GraphQL.NET error here is a known gap ({string.Join(", ", gaps.Select(_ => $"{_.Rule} — {upstream}/blob/master/{knownGaps[_.Rule]}"))}), " +
                    "so BlazorQL was expected to stay silent. If a gap has been closed, remove it from knownGaps.");
            }
            else
            {
                Assert.That(
                    mine,
                    Is.Empty,
                    $"GraphQL.NET considers this document valid, but BlazorQL reported: {string.Join("; ", mine)}");
            }
        }

        // The wording is not asserted — it is reviewed. A snapshot puts both sides' messages side
        // by side so a divergence that is not a pass/fail difference is still visible in a diff.
        await Verify(
                new
                {
                    query,
                    graphQLDotNet = reference.Select(_ => $"[{_.Number} {_.Rule}] {_.Message}"),
                    blazorQL = mine,
                    divergence = knownDivergences.GetValueOrDefault(name)
                })
            .UseParameters(name);
    }

    /// <summary>
    /// GraphQL.NET's answer. Documents that do not parse are not interesting here — both sides
    /// report the syntax error and nothing else — so the corpus is assumed parseable.
    /// </summary>
    static async Task<IReadOnlyList<(string Rule, string Number, string Message)>> Reference(string query)
    {
        var document = Parser.Parse(query);
        var result = await new DocumentValidator().ValidateAsync(
            new()
            {
                Schema = schema,
                Document = document,
                Operation = document.Definitions.OfType<GraphQLOperationDefinition>().FirstOrDefault()!,
                UserContext = new Dictionary<string, object?>(),
                Variables = Inputs.Empty,
                Extensions = Inputs.Empty
            });

        IEnumerable<ExecutionError> errors = result.Errors;
        return
        [
            .. errors
                .OfType<ValidationError>()
                .Select(_ => (Rule: _.GetType().Name, Number: _.Number ?? "?", Message: _.Message))
                .OrderBy(_ => _.Rule, StringComparer.Ordinal)
                .ThenBy(_ => _.Message, StringComparer.Ordinal)
        ];
    }

    /// <summary>BlazorQL's answer, errors only — deprecation warnings have no counterpart.</summary>
    static IReadOnlyList<string> Mine(string query) =>
    [
        .. new SchemaValidator(index)
            .Validate(DocumentInfo.Parse(query))
            .Where(_ => _.IsError)
            .Select(_ => _.Message)
    ];

    static readonly SampleSchema schema = new();
    static SchemaIndex index = null!;

    /// <summary>
    /// The bridge that makes this a differential test rather than two fixtures: BlazorQL's own
    /// introspection query, executed against the same schema object GraphQL.NET validates over,
    /// parsed by the same <see cref="SchemaIndex"/> the IDE builds at runtime. The draft additions
    /// are left off because GraphQL.NET does not serve them.
    /// </summary>
    [OneTimeSetUp]
    public async Task BuildIndex()
    {
        var result = await new DocumentExecuter().ExecuteAsync(new()
        {
            Schema = schema,
            Query = BlazorQLIde.IntrospectionQuery(draftAdditions: false)
        });

        Assert.That(result.Errors, Is.Null.Or.Empty);

        using var document = JsonDocument.Parse(new GraphQLSerializer().Serialize(result));
        index = SchemaIndex.Parse(document.RootElement.GetProperty("data"))!;
        Assert.That(index, Is.Not.Null);
    }

    static string Describe(IEnumerable<(string Rule, string Number, string Message)> errors) =>
        string.Join("; ", errors.Select(_ => $"[{_.Rule}] {_.Message}"));
}
