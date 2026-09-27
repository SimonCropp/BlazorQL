/// <summary>
/// What the response pane puts an image preview behind. The token handed here is whatever sits
/// between two boundary characters on the hovered line, so it is a whole JSON string value — which
/// is why the no answers matter more than the yes ones: nearly every hover is one.
/// </summary>
public class ImageTokenTests
{
    [Test]
    [Arguments("avatar.png")]
    [Arguments("diagram.svg")]
    [Arguments("photo.jpg")]
    [Arguments("photo.jpeg")]
    [Arguments("loop.gif")]
    [Arguments("shot.webp")]
    [Arguments("https://example.com/assets/avatars/user-1234.png")]
    [Arguments("/relative/path/to/thing.png")]
    public async Task AnImageIsRecognised(string token) =>
        await Assert.That(ImageToken.IsImage(token)).IsTrue();

    /// <summary>Extensions are matched whatever their case, which is what IgnoreCase is there for.</summary>
    [Test]
    [Arguments("AVATAR.PNG")]
    [Arguments("Photo.JpEg")]
    [Arguments("HTTPS://EXAMPLE.COM/USER.GIF")]
    public async Task CaseDoesNotMatter(string token) =>
        await Assert.That(ImageToken.IsImage(token)).IsTrue();

    [Test]
    [Arguments("")]
    [Arguments("abc123")]
    [Arguments("png")]
    [Arguments(".png")]
    [Arguments("report.pdf")]
    [Arguments("archive.png.zip")]
    [Arguments("https://example.com/user-1234.pngx")]
    [Arguments("2026-09-04T06:20:00Z")]
    public async Task AnythingElseIsNot(string token) =>
        await Assert.That(ImageToken.IsImage(token)).IsFalse();

    /// <summary>
    /// A long value with no dot in it is the shape the pane hands over most — an id, a token, a
    /// base64 blob — and the one the matcher used to spend the longest saying no to.
    /// </summary>
    [Test]
    public async Task ALongValueWithNoExtensionIsNot() =>
        await Assert.That(ImageToken.IsImage(new('a', 200))).IsFalse();

    /// <summary>The extension has to end the token, because the token is the whole value.</summary>
    [Test]
    public async Task AnExtensionMidTokenIsNot() =>
        await Assert.That(ImageToken.IsImage("avatar.png?width=200")).IsFalse();
}