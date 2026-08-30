namespace BlazorQL.Sample;

/// <summary>
/// What the sample's first tab opens with, in place of the component's generic welcome text: a
/// short orientation comment, then a query that exercises most of <see cref="SampleSchema"/> —
/// variables with defaults, an alias, arguments of several shapes, nested objects and lists, and a
/// union spread through a fragment. Both variables default, so it runs as-is.
/// </summary>
static class DemoQuery
{
    public const string Text =
        """
        # BlazorQL — an in-browser GraphQL IDE for Blazor, in C#.
        #
        # This page has no backend: the schema is a C# port of
        # GraphiQL's test schema, executed right here in your
        # browser by GraphQL.NET.
        #
        # Press the play button (or Ctrl-Enter) to run the query
        # below. Space (or typing) completes, Shift-Ctrl-P
        # prettifies, Shift-Ctrl-M merges fragments, and hovering
        # a field shows its docs.
        #
        # Also worth running:
        #
        #   subscription { message(delay: 300) }
        #   mutation { setString(value: "hi") }
        #
        # The endpoint box above aims this same UI at a real API:
        # an http(s) url posts over HTTP, ws(s) speaks
        # graphql-transport-ws, and clearing it comes back to the
        # built-in schema.

        query Demo($greeting: String = "hello", $color: TestEnum = RED) {
          id
          isTest
          args: hasArgs(string: $greeting, int: 42, enum: $color, listString: ["a", "b"])
          person {
            name
            age(delay: 200)
            friends {
              name
            }
          }
          deferrable {
            normalString
            deferredString(delay: 300)
          }
          streamable(delay: 100) {
            text
          }
          union {
            ...unionName
          }
        }

        fragment unionName on TestUnion {
          ... on First {
            name
          }
          ... on Second {
            name
          }
        }

        """;
}
