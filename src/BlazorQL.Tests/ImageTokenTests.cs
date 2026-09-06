/// <summary>
/// What the response pane puts an image preview behind. The token handed here is whatever sits
/// between two boundary characters on the hovered line, so it is a whole JSON string value — which
/// is why the no answers matter more than the yes ones: nearly every hover is one.
/// </summary>
[TestFixture]
public class ImageTokenTests
{
    [TestCase("avatar.png")]
    [TestCase("diagram.svg")]
    [TestCase("photo.jpg")]
    [TestCase("photo.jpeg")]
    [TestCase("loop.gif")]
    [TestCase("shot.webp")]
    [TestCase("https://example.com/assets/avatars/user-1234.png")]
    [TestCase("/relative/path/to/thing.png")]
    public void AnImageIsRecognised(string token) =>
        Assert.That(ImageToken.IsImage(token), Is.True);

    /// <summary>Extensions are matched whatever their case, which is what IgnoreCase is there for.</summary>
    [TestCase("AVATAR.PNG")]
    [TestCase("Photo.JpEg")]
    [TestCase("HTTPS://EXAMPLE.COM/USER.GIF")]
    public void CaseDoesNotMatter(string token) =>
        Assert.That(ImageToken.IsImage(token), Is.True);

    [TestCase("")]
    [TestCase("abc123")]
    [TestCase("png")]
    [TestCase(".png")]
    [TestCase("report.pdf")]
    [TestCase("archive.png.zip")]
    [TestCase("https://example.com/user-1234.pngx")]
    [TestCase("2026-09-04T06:20:00Z")]
    public void AnythingElseIsNot(string token) =>
        Assert.That(ImageToken.IsImage(token), Is.False);

    /// <summary>
    /// A long value with no dot in it is the shape the pane hands over most — an id, a token, a
    /// base64 blob — and the one the matcher used to spend the longest saying no to.
    /// </summary>
    [Test]
    public void ALongValueWithNoExtensionIsNot() =>
        Assert.That(ImageToken.IsImage(new('a', 200)), Is.False);

    /// <summary>The extension has to end the token, because the token is the whole value.</summary>
    [Test]
    public void AnExtensionMidTokenIsNot() =>
        Assert.That(ImageToken.IsImage("avatar.png?width=200"), Is.False);
}
