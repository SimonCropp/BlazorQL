/// <summary>
/// Reads a <c>multipart/mixed</c> body one part at a time, handing each part over the moment the
/// delimiter that ends it arrives.
/// </summary>
/// <remarks>
/// Incremental delivery is the only reason the body is multipart, so when a part is handed over is
/// the whole point. A general-purpose reader ends a part only once it has seen what follows the
/// delimiter (the "--" of the close, or the line break before the next part's headers), and GraphQL
/// Yoga writes that only when the next part is ready: each part ends with <c>\r\n---</c> and nothing
/// more. Against Yoga, every @defer payload waited for the one after it, and the initial payload
/// for the first deferred one, which is to say @defer did nothing visible.
/// <para>
/// A part's body runs to the next delimiter, CRLF "--" boundary, per RFC 2046. A part's headers
/// (Content-Type, Content-Length) are skipped: the parts of an incremental response are all JSON,
/// and the delimiter alone says where each ends.
/// </para>
/// </remarks>
static class MultipartParts
{
    static readonly byte[] lineBreak = "\r\n"u8.ToArray();
    static readonly byte[] blankLine = "\r\n\r\n"u8.ToArray();

    public static async IAsyncEnumerable<MemoryStream> Read(Stream stream, string boundary, [EnumeratorCancellation] Cancel cancel)
    {
        var dashBoundary = Encoding.ASCII.GetBytes("--" + boundary);
        var delimiter = Encoding.ASCII.GetBytes("\r\n--" + boundary);
        var buffer = new ReadBuffer(stream);

        // Anything before the first delimiter is preamble.
        if (!await buffer.SkipPast(dashBoundary, cancel))
        {
            yield break;
        }

        while (true)
        {
            // Straight after a delimiter: "--" closes the body. Anything else is transport padding
            // up to the end of the delimiter's line.
            if (!await buffer.Ensure(2, cancel) ||
                buffer.StartsWith("--"u8) ||
                !await buffer.SkipPast(lineBreak, cancel))
            {
                yield break;
            }

            // The headers end at a blank line, and a part with none starts with it.
            if (!await buffer.Ensure(2, cancel))
            {
                yield break;
            }

            if (buffer.StartsWith(lineBreak))
            {
                buffer.Consume(lineBreak.Length);
            }
            else if (!await buffer.SkipPast(blankLine, cancel))
            {
                yield break;
            }

            var end = await buffer.IndexOf(delimiter, cancel);
            if (end < 0)
            {
                // The body stopped without closing the part. What arrived is still handed over;
                // if it is cut short, it fails as JSON with the text in the message.
                yield return buffer.Take(buffer.Count);
                yield break;
            }

            var part = buffer.Take(end);
            buffer.Consume(delimiter.Length);
            yield return part;
        }
    }

    /// <summary>
    /// The bytes read from the stream and not yet consumed. Offsets are relative to the first
    /// unconsumed byte, so they survive the compaction a read can do.
    /// </summary>
    sealed class ReadBuffer(Stream stream)
    {
        byte[] data = new byte[16 * 1024];
        int start;

        public int Count { get; private set; }

        ReadOnlySpan<byte> Unconsumed => data.AsSpan(start, Count);

        public bool StartsWith(ReadOnlySpan<byte> value) =>
            Unconsumed.StartsWith(value);

        public void Consume(int length)
        {
            start += length;
            Count -= length;
        }

        /// <summary>Copies out the next <paramref name="length"/> bytes and consumes them.</summary>
        public MemoryStream Take(int length)
        {
            var bytes = Unconsumed[..length].ToArray();
            Consume(length);
            return new(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        }

        /// <summary>Reads until at least <paramref name="length"/> bytes are held. False at the end of the stream.</summary>
        public async Task<bool> Ensure(int length, Cancel cancel)
        {
            while (Count < length)
            {
                if (!await Fill(cancel))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Where <paramref name="value"/> next starts, reading as far as it takes. -1 if the stream
        /// ends first. Each read only searches what could newly complete a match.
        /// </summary>
        public async Task<int> IndexOf(byte[] value, Cancel cancel)
        {
            var from = 0;
            while (true)
            {
                var found = Unconsumed[from..].IndexOf(value);
                if (found >= 0)
                {
                    return from + found;
                }

                from = Math.Max(0, Count - value.Length + 1);
                if (!await Fill(cancel))
                {
                    return -1;
                }
            }
        }

        /// <summary>Consumes everything up to and including <paramref name="value"/>. False if the stream ends first.</summary>
        public async Task<bool> SkipPast(byte[] value, Cancel cancel)
        {
            var index = await IndexOf(value, cancel);
            if (index < 0)
            {
                return false;
            }

            Consume(index + value.Length);
            return true;
        }

        /// <summary>One read from the stream: whatever has arrived, which for a streamed body is one chunk.</summary>
        async Task<bool> Fill(Cancel cancel)
        {
            if (start > 0)
            {
                Unconsumed.CopyTo(data);
                start = 0;
            }

            if (Count == data.Length)
            {
                Array.Resize(ref data, data.Length * 2);
            }

            var read = await stream.ReadAsync(data.AsMemory(Count), cancel);
            Count += read;
            return read > 0;
        }
    }
}
