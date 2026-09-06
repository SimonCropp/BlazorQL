/// <summary>
/// Whether a token in the response pane names an image, which is what puts a preview behind a hover
/// over it. A token is whatever sits between two boundary characters on the line, so this is asked
/// of a whole JSON string value — a url, an id, a base64 blob — on every hover the pane answers.
/// </summary>
static partial class ImageToken
{
    // Source-generated rather than constructed. A WebAssembly build runs the regex interpreter --
    // RegexOptions.Compiled needs codegen that is not there once the app is AOT-compiled -- and the
    // interpreter is slowest on the tokens this says no to, which is nearly all of them: \S+ takes
    // the whole value and then gives it back a character at a time looking for the dot. Over a
    // 200-character value that is 1.9 us interpreted against 0.12 us generated.
    //
    // The accessibility modifier is not a style choice here: a partial method returning a value has
    // to carry one.
    [GeneratedRegex(@"\S+\.(png|svg|jpe?g|gif|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool IsImage(string token) =>
        Pattern().IsMatch(token);
}
