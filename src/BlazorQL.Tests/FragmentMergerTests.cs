/// <summary>
/// Inlining named fragments. The cases that matter are the ones a naive recursion cannot survive: a
/// fragment that spreads itself, a pair that spread each other, and a duplicate definition name.
/// </summary>
[TestFixture]
public class FragmentMergerTests
{
    [Test]
    public Task InlinesASpreadIntoItsOperation()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...F
              }
            }

            fragment F on Person {
              name
            }
            """);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return Verify(text);
    }

    [Test]
    public void ASelfSpreadingFragmentIsRefusedRatherThanInlined()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...F
              }
            }

            fragment F on Person {
              name
              ...F
            }
            """);

        Assert.That(ok, Is.False);
        Assert.That(text, Is.Null);
        Assert.That(error, Is.EqualTo("""Cannot spread fragment "F" within itself."""));
    }

    [Test]
    public void APairOfFragmentsSpreadingEachOtherIsRefused()
    {
        var (ok, _, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...A
              }
            }

            fragment A on Person {
              name
              ...B
            }

            fragment B on Person {
              ...A
            }
            """);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo("""Cannot spread fragment "A" within itself via "B"."""));
    }

    [Test]
    public void ACycleThroughANestedSelectionIsFound()
    {
        var (ok, _, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...A
              }
            }

            fragment A on Person {
              friends {
                ...B
              }
            }

            fragment B on Person {
              ... on Person {
                ...A
              }
            }
            """);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo("""Cannot spread fragment "A" within itself via "B"."""));
    }

    /// <summary>
    /// A spread carrying a directive is not inlined, so this document would not have overflowed. It
    /// is still refused: any cycle makes the document invalid, and a partial merge would hide it.
    /// </summary>
    [Test]
    public void ACycleBehindADirectiveIsStillRefused()
    {
        var (ok, _, error) = FragmentMerger.Merge(
            """
            query ($s: Boolean!) {
              person {
                ...F
              }
            }

            fragment F on Person {
              name
              ...F @include(if: $s)
            }
            """);

        Assert.That(ok, Is.False);
        Assert.That(error, Is.EqualTo("""Cannot spread fragment "F" within itself."""));
    }

    /// <summary>Two definitions of one name is a validator error, not something Merge may throw on.</summary>
    [Test]
    public Task ADuplicateFragmentNameTakesTheFirstDefinition()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...F
              }
            }

            fragment F on Person {
              name
            }

            fragment F on Person {
              id
            }
            """);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return Verify(text);
    }

    [Test]
    public Task AnUnknownSpreadIsLeftAlone()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...Missing
              }
            }
            """);

        Assert.That(error, Is.Null);
        Assert.That(ok);
        return Verify(text);
    }
}
