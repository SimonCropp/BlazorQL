/// <summary>
/// Inlining named fragments. The cases that matter are the ones a naive recursion cannot survive: a
/// fragment that spreads itself, a pair that spread each other, and a duplicate definition name.
/// </summary>
public class FragmentMergerTests
{
    [Test]
    public async Task InlinesASpreadIntoItsOperation()
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

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Verify(text);
    }

    [Test]
    public async Task ASelfSpreadingFragmentIsRefusedRatherThanInlined()
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

        await Assert.That(ok).IsFalse();
        await Assert.That(text).IsNull();
        await Assert.That(error).IsEqualTo("""Cannot spread fragment "F" within itself.""");
    }

    [Test]
    public async Task APairOfFragmentsSpreadingEachOtherIsRefused()
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

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo("""Cannot spread fragment "A" within itself via "B".""");
    }

    [Test]
    public async Task ACycleThroughANestedSelectionIsFound()
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

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo("""Cannot spread fragment "A" within itself via "B".""");
    }

    /// <summary>
    /// A spread carrying a directive is not inlined, so this document would not have overflowed. It
    /// is still refused: any cycle makes the document invalid, and a partial merge would hide it.
    /// </summary>
    [Test]
    public async Task ACycleBehindADirectiveIsStillRefused()
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

        await Assert.That(ok).IsFalse();
        await Assert.That(error).IsEqualTo("""Cannot spread fragment "F" within itself.""");
    }

    /// <summary>Two definitions of one name is a validator error, not something Merge may throw on.</summary>
    [Test]
    public async Task ADuplicateFragmentNameTakesTheFirstDefinition()
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

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Verify(text);
    }

    [Test]
    public async Task AnUnknownSpreadIsLeftAlone()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query {
              person {
                ...Missing
              }
            }
            """);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Verify(text);
    }

    /// <summary>
    /// A spread carrying a directive is deliberately not inlined, so removing its definition would
    /// leave the document spreading a fragment that is no longer there.
    /// </summary>
    [Test]
    public async Task ADefinitionStillSpreadBehindADirectiveIsKept()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query ($s: Boolean!) {
              person {
                ...F @include(if: $s)
              }
            }

            fragment F on Person {
              name
            }
            """);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(DocumentInfo.Parse(text!).Fragments).Count().IsEqualTo(1);
        await Verify(text);
    }

    /// <summary>What a kept definition spreads has to be kept as well, however deep the chain.</summary>
    [Test]
    public async Task WhatAKeptDefinitionSpreadsIsKeptToo()
    {
        var (ok, text, error) = FragmentMerger.Merge(
            """
            query ($s: Boolean!) {
              person {
                ...A @include(if: $s)
              }
            }

            fragment A on Person {
              ...B
            }

            fragment B on Person {
              name
            }
            """);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Verify(text);
    }

    /// <summary>A definition nothing spreads any more still goes.</summary>
    [Test]
    public async Task AnInlinedDefinitionIsStillRemoved()
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

            fragment Unused on Person {
              id
            }
            """);

        await Assert.That(error).IsNull();
        await Assert.That(ok).IsTrue();
        await Assert.That(DocumentInfo.Parse(text!).Fragments).IsEmpty();
        await Verify(text);
    }
}