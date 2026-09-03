/// <summary>
/// Offset to line/column and back, over one text. Every diagnostic marker and every automatically
/// inserted leaf goes through this, so the answers have to match a scan exactly — including at the
/// edges, where nothing has to be right for the common case to look right.
/// </summary>
[TestFixture]
public class LineIndexTests
{
    const string text = "one\ntwo\n\nfour";

    static readonly LineIndex lines = new(text);

    [TestCase(1, 1, 0)]
    [TestCase(1, 4, 3)]
    [TestCase(2, 1, 4)]
    [TestCase(3, 1, 8)]
    [TestCase(4, 1, 9)]
    [TestCase(4, 5, 13)]
    public void OffsetOfALineAndColumn(int line, int column, int expected) =>
        Assert.That(lines.Offset(line, column), Is.EqualTo(expected));

    /// <summary>Out of range in either direction lands somewhere in the text rather than throwing.</summary>
    [TestCase(0, 1, 0)]
    [TestCase(1, 0, 0)]
    [TestCase(5, 1, 13)]
    [TestCase(4, 99, 13)]
    public void OffsetIsClampedToTheText(int line, int column, int expected) =>
        Assert.That(lines.Offset(line, column), Is.EqualTo(expected));

    [TestCase(0, 1, 1)]
    [TestCase(3, 1, 4)]
    [TestCase(4, 2, 1)]
    [TestCase(7, 2, 4)]
    [TestCase(8, 3, 1)]
    [TestCase(9, 4, 1)]
    [TestCase(13, 4, 5)]
    public void LineAndColumnOfAnOffset(int offset, int line, int column) =>
        Assert.That(lines.LineColumn(offset), Is.EqualTo((line, column)));

    [TestCase(-5, 1, 1)]
    [TestCase(99, 4, 5)]
    public void LineColumnIsClampedToTheText(int offset, int line, int column) =>
        Assert.That(lines.LineColumn(offset), Is.EqualTo((line, column)));

    [Test]
    public void AnEmptyTextIsOneEmptyLine()
    {
        var empty = new LineIndex("");

        Assert.That(empty.Offset(1, 1), Is.Zero);
        Assert.That(empty.LineColumn(0), Is.EqualTo((1, 1)));
    }

    [Test]
    public void ATrailingNewlineOpensALastLine()
    {
        var trailing = new LineIndex("a\n");

        Assert.That(trailing.Offset(2, 1), Is.EqualTo(2));
        Assert.That(trailing.LineColumn(2), Is.EqualTo((2, 1)));
    }

    [Test]
    public void ARangeSpansTwoOffsets()
    {
        var range = lines.Range(4, 7);

        Assert.That(range.StartLineNumber, Is.EqualTo(2));
        Assert.That(range.StartColumn, Is.EqualTo(1));
        Assert.That(range.EndLineNumber, Is.EqualTo(2));
        Assert.That(range.EndColumn, Is.EqualTo(4));
    }

    /// <summary>
    /// The differential check: every offset in a document with blank lines, long lines and a
    /// trailing newline agrees with the scan the index replaced.
    /// </summary>
    [Test]
    public void EveryOffsetAgreesWithAScan()
    {
        var document = "query Q {\n  a\n\n    b(x: 1)\n}\n\n# trailing comment\n";
        var index = new LineIndex(document);

        for (var offset = 0; offset <= document.Length; offset++)
        {
            Assert.That(index.LineColumn(offset), Is.EqualTo(Scan(document, offset)), $"offset {offset}");
        }

        for (var line = 1; line <= 8; line++)
        {
            for (var column = 1; column <= 20; column++)
            {
                Assert.That(index.Offset(line, column), Is.EqualTo(ScanOffset(document, line, column)), $"{line}:{column}");
            }
        }
    }

    static (int Line, int Column) Scan(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset && index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    static int ScanOffset(string text, int line, int column)
    {
        var offset = 0;
        var current = 1;
        while (current < line && offset < text.Length)
        {
            if (text[offset] == '\n')
            {
                current++;
            }

            offset++;
        }

        return Math.Min(offset + (column - 1), text.Length);
    }
}
