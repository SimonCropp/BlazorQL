using GraphQL.Execution;
using GraphQL.Resolvers;
using GraphQL.Types;

namespace BlazorQL.Sample;

/// <summary>
/// A GraphQL.NET port of GraphiQL's test schema (graphql/graphiql, MIT, GraphQL Contributors).
/// Names, types, descriptions, deprecations, and argument defaults are kept exactly as the
/// original graphql-js schema declared them; resolvers are ported to C#. Two knowing deviations:
/// GraphQL.NET has no incremental delivery, so the <c>@defer</c>/<c>@stream</c> directives are
/// absent, and the <c>JSON</c> scalar accepts literals instead of throwing.
/// </summary>
public sealed class SampleSchema :
    Schema
{
    public SampleSchema()
    {
        Description = "This is a test schema for GraphiQL";

        var json = new ComplexScalarGraphType
        {
            Name = "JSON",
            Description = "A scalar that accepts arbitrary JSON values."
        };

        var testEnum = new EnumerationGraphType
        {
            Name = "TestEnum",
            Description = "An enum of super cool colors."
        };
        testEnum.Add("RED", "RED", "A rosy color");
        testEnum.Add("GREEN", "GREEN", "The color of martians and slime");
        testEnum.Add("BLUE", "BLUE", "A feeling you might have if you can't use GraphQL");
        testEnum.Add("GRAY", "GRAY", "A really dull color", "Colors are available now.");

        var testInput = new InputObjectGraphType
        {
            Name = "TestInput",
            Description = "Test all sorts of inputs in this input object type."
        };
        testInput.AddField(new()
        {
            Name = "string",
            Type = typeof(StringGraphType),
            Description = "Repeats back this string"
        });
        testInput.AddField(new() {Name = "int", Type = typeof(IntGraphType)});
        testInput.AddField(new() {Name = "float", Type = typeof(FloatGraphType)});
        testInput.AddField(new() {Name = "boolean", Type = typeof(BooleanGraphType)});
        testInput.AddField(new() {Name = "id", Type = typeof(IdGraphType)});
        testInput.AddField(new() {Name = "enum", ResolvedType = testEnum});
        testInput.AddField(new() {Name = "object", ResolvedType = testInput});
        testInput.AddField(new()
        {
            Name = "defaultValueString",
            Type = typeof(StringGraphType),
            DefaultValue = "test default value"
        });
        testInput.AddField(new()
        {
            Name = "defaultValueBoolean",
            Type = typeof(BooleanGraphType),
            DefaultValue = false
        });
        testInput.AddField(new()
        {
            Name = "defaultValueInt",
            Type = typeof(IntGraphType),
            DefaultValue = 5
        });
        testInput.AddField(new() {Name = "listString", Type = typeof(ListGraphType<StringGraphType>)});
        testInput.AddField(new() {Name = "listInt", Type = typeof(ListGraphType<IntGraphType>)});
        testInput.AddField(new() {Name = "listFloat", Type = typeof(ListGraphType<FloatGraphType>)});
        testInput.AddField(new() {Name = "listBoolean", Type = typeof(ListGraphType<BooleanGraphType>)});
        testInput.AddField(new() {Name = "listID", Type = typeof(ListGraphType<IdGraphType>)});
        testInput.AddField(new() {Name = "listEnum", ResolvedType = new ListGraphType(testEnum)});
        testInput.AddField(new() {Name = "listObject", ResolvedType = new ListGraphType(testInput)});

        var testInterface = new InterfaceGraphType
        {
            Name = "TestInterface",
            Description = "Test interface."
        };
        testInterface.AddField(new()
        {
            Name = "name",
            Type = typeof(StringGraphType),
            Description = "Common name string."
        });

        var first = new ObjectGraphType {Name = "First"};
        var second = new ObjectGraphType {Name = "Second"};
        // The original: resolveType(check) => check ? First : Second — i.e. only an explicit
        // falsy source resolves to Second.
        testInterface.ResolveType = _ => _ is false ? second : first;

        first.AddField(new()
        {
            Name = "name",
            Type = typeof(StringGraphType),
            // The original repeats "UnionFirst" on both types.
            Description = "Common name string for UnionFirst.",
            Resolver = new FuncFieldResolver<object?>(_ => null)
        });
        first.AddField(new()
        {
            Name = "first",
            ResolvedType = new ListGraphType(testInterface),
            // Faithful to the original's resolve: () => true — erroring if actually selected.
            Resolver = new FuncFieldResolver<object?>(_ => true)
        });
        first.AddResolvedInterface(testInterface);

        second.AddField(new()
        {
            Name = "name",
            Type = typeof(StringGraphType),
            Description = "Common name string for UnionFirst.",
            Resolver = new FuncFieldResolver<object?>(_ => null)
        });
        second.AddField(new()
        {
            Name = "second",
            ResolvedType = testInterface,
            Resolver = new FuncFieldResolver<object?>(_ => false)
        });
        second.AddResolvedInterface(testInterface);

        var testUnion = new UnionGraphType {Name = "TestUnion"};
        testUnion.AddPossibleType(first);
        testUnion.AddPossibleType(second);
        testUnion.ResolveType = _ => first;

        var greeting = new ObjectGraphType {Name = "Greeting"};
        greeting.AddField(new()
        {
            Name = "text",
            Type = typeof(StringGraphType),
            Resolver = new FuncFieldResolver<object?>(_ => (_.Source as GreetingSource)?.Text)
        });

        var deferrable = new ObjectGraphType {Name = "Deferrable"};
        deferrable.AddField(new()
        {
            Name = "normalString",
            Type = typeof(StringGraphType),
            Resolver = new FuncFieldResolver<object?>(_ => "Nice")
        });
        deferrable.AddField(new()
        {
            Name = "deferredString",
            Type = typeof(StringGraphType),
            Arguments = new(DelayArgument(600)),
            Resolver = new FuncFieldResolver<object?>(async _ =>
            {
                var delay = _.GetArgument("delay", 600);
                var seconds = delay / 1000d;
                await Task.Delay(delay, _.CancellationToken);
                return FormattableString.Invariant(
                    $"Oops, this took {seconds} seconds longer than I thought it would!");
            })
        });

        var person = new ObjectGraphType {Name = "Person"};
        person.AddField(new()
        {
            Name = "name",
            Type = typeof(StringGraphType),
            Resolver = new FuncFieldResolver<object?>(_ => (_.Source as PersonSource)?.Name)
        });
        person.AddField(new()
        {
            Name = "age",
            Type = typeof(IntGraphType),
            Arguments = new(DelayArgument(600)),
            Resolver = new FuncFieldResolver<object?>(async _ =>
            {
                var delay = _.GetArgument("delay", 600);
                await Task.Delay(delay, _.CancellationToken);
                // The original returns Math.ceil(args.delay).
                return delay;
            })
        });
        person.AddField(new()
        {
            Name = "friends",
            ResolvedType = new ListGraphType(person),
            // Top 4 names https://ssa.gov/oact/babynames/decades/century.html (the original
            // yields them one by one from an async generator; a plain list resolves the same).
            Resolver = new FuncFieldResolver<object?>(_ => new PersonSource[]
            {
                new("James"),
                new("Mary"),
                new("John"),
                new("Patrica")
            })
        });

        var testType = new ObjectGraphType
        {
            Name = "Test",
            Description = "Test type for testing\n New line works"
        };
        testType.AddField(new()
        {
            Name = "test",
            ResolvedType = testType,
            Description = "`test` field from `Test` type.",
            Resolver = new FuncFieldResolver<object?>(_ => new object())
        });
        testType.AddField(new()
        {
            Name = "deferrable",
            ResolvedType = deferrable,
            Resolver = new FuncFieldResolver<object?>(_ => new object())
        });
        testType.AddField(new()
        {
            Name = "streamable",
            ResolvedType = new ListGraphType(greeting),
            Arguments = new(DelayArgument(300)),
            // The original trickles these out with @stream; without incremental delivery the
            // whole list resolves at once.
            Resolver = new FuncFieldResolver<object?>(_ => greetings.Select(text => new GreetingSource(text)))
        });
        testType.AddField(new()
        {
            Name = "person",
            ResolvedType = person,
            Resolver = new FuncFieldResolver<object?>(_ => new PersonSource("Mark"))
        });
        testType.AddField(new()
        {
            Name = "longDescriptionType",
            ResolvedType = testType,
            Description = longDescription,
            Resolver = new FuncFieldResolver<object?>(_ => new object())
        });
        testType.AddField(new()
        {
            Name = "union",
            ResolvedType = testUnion,
            Resolver = new FuncFieldResolver<object?>(_ => new object())
        });
        testType.AddField(new()
        {
            Name = "id",
            Type = typeof(IdGraphType),
            Description = "id field from Test type.",
            Resolver = new FuncFieldResolver<object?>(_ => "abc123")
        });
        testType.AddField(new()
        {
            Name = "isTest",
            Type = typeof(BooleanGraphType),
            Description = "Is this a test schema? Sure it is.",
            Resolver = new FuncFieldResolver<object?>(_ => true)
        });
        testType.AddField(new()
        {
            Name = "image",
            Type = typeof(StringGraphType),
            Description = "field that returns an image URI.",
            Resolver = new FuncFieldResolver<object?>(_ => "/resources/logo.svg")
        });
        testType.AddField(new()
        {
            Name = "deprecatedField",
            ResolvedType = testType,
            Description = "This field is an example of a deprecated field",
            DeprecationReason = "No longer in use, try `test` instead.",
            Resolver = new FuncFieldResolver<object?>(_ => null)
        });
        testType.AddField(new()
        {
            Name = "alsoDeprecated",
            ResolvedType = testType,
            Description = "This field is an example of a deprecated field with markdown in its deprecation reason",
            DeprecationReason = longDescription,
            Resolver = new FuncFieldResolver<object?>(_ => null)
        });
        testType.AddField(new()
        {
            Name = "hasArgs",
            Type = typeof(StringGraphType),
            Arguments = new(
                new QueryArgument<StringGraphType> {Name = "string", Description = "A string"},
                new QueryArgument<IntGraphType> {Name = "int"},
                new QueryArgument<FloatGraphType> {Name = "float"},
                new QueryArgument<BooleanGraphType> {Name = "boolean"},
                new QueryArgument<IdGraphType> {Name = "id"},
                new QueryArgument(testEnum) {Name = "enum"},
                new QueryArgument(testInput) {Name = "object"},
                new QueryArgument(json)
                {
                    Name = "json",
                    Description = "A custom scalar that accepts any JSON value"
                },
                new QueryArgument<StringGraphType>
                {
                    Name = "defaultValue",
                    DefaultValue = "test default value"
                },
                new QueryArgument<ListGraphType<StringGraphType>> {Name = "listString"},
                new QueryArgument<ListGraphType<IntGraphType>> {Name = "listInt"},
                new QueryArgument<ListGraphType<FloatGraphType>> {Name = "listFloat"},
                new QueryArgument<ListGraphType<BooleanGraphType>> {Name = "listBoolean"},
                new QueryArgument<ListGraphType<IdGraphType>> {Name = "listID"},
                new QueryArgument(new ListGraphType(testEnum)) {Name = "listEnum"},
                new QueryArgument(new ListGraphType(testInput)) {Name = "listObject"},
                new QueryArgument<StringGraphType>
                {
                    Name = "deprecatedArg",
                    Description = "Hello!",
                    DeprecationReason = "Argument \"deprecatedArg\" is deprecated. Use \"string\" instead."
                }),
            Resolver = new FuncFieldResolver<object?>(ResolveHasArgs)
        });

        var mutation = new ObjectGraphType
        {
            Name = "MutationType",
            Description = "This is a simple mutation type"
        };
        mutation.AddField(new()
        {
            Name = "setString",
            Type = typeof(StringGraphType),
            Description = "Set the string field",
            Arguments = new(new QueryArgument<StringGraphType> {Name = "value"}),
            // The original has no resolver (its root has no setString, so it returns null);
            // echoing the argument back is more useful for a demo mutation.
            Resolver = new FuncFieldResolver<object?>(_ => _.GetArgument<string?>("value"))
        });

        var subscription = new ObjectGraphType
        {
            Name = "SubscriptionType",
            Description = "This is a simple subscription type. Learn more at https://npmjs.com/package/graphql-ws"
        };
        subscription.AddField(new()
        {
            Name = "message",
            Type = typeof(StringGraphType),
            Description = "Subscribe to a message",
            Arguments = new(DelayArgument(600)),
            StreamResolver = new SourceStreamResolver<string>(_ =>
                new GreetingObservable(_.GetArgument("delay", 600))),
            Resolver = new FuncFieldResolver<object?>(_ => _.Source)
        });

        Query = testType;
        Mutation = mutation;
        Subscription = subscription;
    }

    static QueryArgument DelayArgument(int defaultValue) =>
        new QueryArgument<IntGraphType>
        {
            Name = "delay",
            Description = "delay in milliseconds for subsequent results, for demonstration purposes",
            DefaultValue = defaultValue
        };

    static readonly string[] greetings =
    [
        "Hi",
        "你好",
        "Hola",
        "أهلاً",
        "Bonjour",
        "سلام",
        "안녕",
        "Ciao",
        "हेलो",
        "Здорово"
    ];

    /// <summary>
    /// The subscription source: five greetings, a delay before each, then complete. A hand-rolled
    /// observable (no Rx dependency) — subscribing spins a task that emits with delays, and
    /// disposing the subscription cancels it.
    /// </summary>
    sealed class GreetingObservable(int delay) :
        IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            var cancellation = new CancelSource();
            _ = EmitAsync(observer, cancellation.Token);
            return new Subscription(cancellation);
        }

        async Task EmitAsync(IObserver<string> observer, Cancel cancel)
        {
            try
            {
                foreach (var message in (string[]) ["Hi", "Bonjour", "Hola", "Ciao", "Zdravo"])
                {
                    if (delay > 0)
                    {
                        await Task.Delay(delay, cancel);
                    }

                    observer.OnNext(message);
                }

                observer.OnCompleted();
            }
            catch (OperationCanceledException)
            {
                // Unsubscribed mid-stream; nothing left to emit.
            }
            catch (Exception exception)
            {
                observer.OnError(exception);
            }
        }

        sealed class Subscription(CancelSource cancellation) :
            IDisposable
        {
            public void Dispose()
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
        }
    }

    /// <summary>
    /// Like the original's <c>JSON.stringify(args)</c>: every argument that was provided or has a
    /// default, serialized as JSON.
    /// </summary>
    static string ResolveHasArgs(IResolveFieldContext context)
    {
        Dictionary<string, object?> arguments = [];
        if (context.Arguments is not null)
        {
            foreach (var (name, argument) in context.Arguments)
            {
                // graphql-js includes provided arguments and coerced defaults; an argument that
                // was neither passed nor defaulted is absent.
                if (argument.Source != ArgumentSource.FieldDefault || argument.Value is not null)
                {
                    arguments[name] = argument.Value;
                }
            }
        }

        return JsonSerializer.Serialize(arguments);
    }

    sealed record PersonSource(string Name);

    sealed record GreetingSource(string Text);

    const string longDescription =
        """
        The `longDescriptionType` field on the `Test` type has a long, verbose, description to test inline field docs.

        > We want to test several `markdown` styles!

        Check out [Markdown](https://markdownguide.org) by the way.

        Some notes:
        - Lists
        - work
          - also nested
          - and with very very very very very very very very very very long items that span multiple lines
        - you get the gist

        TO-DO's:
        1. Open GraphiQL
        1. Write a query
           1. Maybe add some variables
           1. Could also add headers
        1. Send the request

        Example query:
        ```graphql
        {
          test {
            id
          }
          hasArgs(string: "very very very very very long string")
        }
        ```

        And we have a local image:

        ![GraphQL Logo](/resources/logo.svg)

        And external image:

        ![Cat](https://placecats.com/300/200)
        """;
}
