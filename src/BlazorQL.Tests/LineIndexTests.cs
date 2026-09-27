/// <summary>
/// Offset to line/column and back, over one text. Every diagnostic marker and every automatically
/// inserted leaf goes through this, so the answers have to match a scan exactly — including at the
/// edges, where nothing has to be right for the common case to look right.
/// </summary>
public class LineIndexTests
{
    const string text = "one\ntwo\n\nfour";

    static readonly LineIndex lines = new(text);

    [Test]
    [Arguments(1, 1, 0)]
    [Arguments(1, 4, 3)]
    [Arguments(2, 1, 4)]
    [Arguments(3, 1, 8)]
    [Arguments(4, 1, 9)]
    [Arguments(4, 5, 13)]
    public async Task OffsetOfALineAndColumn(int line, int column, int expected) =>
        await Assert.That(lines.Offset(line, column)).IsEqualTo(expected);

    /// <summary>Out of range in either direction lands somewhere in the text rather than throwing.</summary>
    [Test]
    [Arguments(0, 1, 0)]
    [Arguments(1, 0, 0)]
    [Arguments(5, 1, 13)]
    [Arguments(4, 99, 13)]
    public async Task OffsetIsClampedToTheText(int line, int column, int expected) =>
        await Assert.That(lines.Offset(line, column)).IsEqualTo(expected);

    [Test]
    [Arguments(0, 1, 1)]
    [Arguments(3, 1, 4)]
    [Arguments(4, 2, 1)]
    [Arguments(7, 2, 4)]
    [Arguments(8, 3, 1)]
    [Arguments(9, 4, 1)]
    [Arguments(13, 4, 5)]
    public async Task LineAndColumnOfAnOffset(int offset, int line, int column) =>
        await Assert.That(lines.LineColumn(offset)).IsEqualTo((line, column));

    [Test]
    [Arguments(-5, 1, 1)]
    [Arguments(99, 4, 5)]
    public async Task LineColumnIsClampedToTheText(int offset, int line, int column) =>
        await Assert.That(lines.LineColumn(offset)).IsEqualTo((line, column));

    [Test]
    public async Task AnEmptyTextIsOneEmptyLine()
    {
        var empty = new LineIndex("");

        await Assert.That(empty.Offset(1, 1)).IsZero();
        await Assert.That(empty.LineColumn(0)).IsEqualTo((1, 1));
    }

    [Test]
    public async Task ATrailingNewlineOpensALastLine()
    {
        var trailing = new LineIndex("a\n");

        await Assert.That(trailing.Offset(2, 1)).IsEqualTo(2);
        await Assert.That(trailing.LineColumn(2)).IsEqualTo((2, 1));
    }

    [Test]
    public async Task ARangeSpansTwoOffsets()
    {
        var range = lines.Range(4, 7);

        await Assert.That(range.StartLineNumber).IsEqualTo(2);
        await Assert.That(range.StartColumn).IsEqualTo(1);
        await Assert.That(range.EndLineNumber).IsEqualTo(2);
        await Assert.That(range.EndColumn).IsEqualTo(4);
    }

    /// <summary>
    /// The differential check: every offset in a document with blank lines, long lines and a
    /// trailing newline agrees with the scan the index replaced.
    /// </summary>
    [Test]
    public async Task EveryOffsetAgreesWithAScan()
    {
        var document = "query Q {\n  a\n\n    b(x: 1)\n}\n\n# trailing comment\n";
        var index = new LineIndex(document);

        for (var offset = 0; offset <= document.Length; offset++)
        {
            await Assert.That(index.LineColumn(offset)).IsEqualTo(Scan(document, offset)).Because($"offset {offset}");
        }

        for (var line = 1; line <= 8; line++)
        {
            for (var column = 1; column <= 20; column++)
            {
                await Assert.That(index.Offset(line, column)).IsEqualTo(ScanOffset(document, line, column)).Because($"{line}:{column}");
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