namespace ErpApp.Domain.Common;

/// <summary>The three formats the reference product's logo upload accepts, read live off its own
/// hint text ("JPG/PNG/GIF", erp-module-scan.md §5).</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
}

/// <summary>Format and pixel dimensions, as read from the file's own bytes.</summary>
public sealed record ImageHeaderInfo(ImageFormat Format, int Width, int Height)
{
    /// <summary>The media type to serve this image back as. Derived from the bytes, never echoed
    /// from the upload's own <c>Content-Type</c> header, which the client controls.</summary>
    public string ContentType => Format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        _ => "image/gif",
    };

    public string FileExtension => Format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        _ => ".gif",
    };
}

/// <summary>
/// Phase 39 — reads an image's format and dimensions from its header, without decoding it.
///
/// <para><b>This is a format check first and a dimension check second, and the order matters.</b>
/// The reference product's logo rules are "JPG/PNG/GIF, min 300×300, max 5MB". Two of those can be
/// checked from the upload's declared content type and length — and both of those are supplied by
/// the client, so neither is worth anything against a file that is not what it says it is. The
/// organization logo is embedded in every PDF this app sends a customer and served back to every
/// browser that opens the profile page, which makes "is this actually a PNG" the question that
/// matters. A parser that can find the dimensions is one that has already answered it.</para>
///
/// <para><b>No image library.</b> Each of the three formats states its dimensions in a fixed,
/// documented place near the front of the file, and this reads exactly that — roughly sixty lines
/// against a dependency (and its CVE surface, and its native codecs) added for one validation rule.
/// It decodes no pixels, so a malformed or hostile body reaches no decoder at all.</para>
/// </summary>
public static class ImageHeader
{
    /// <summary>How much of the stream is examined. A JPEG's SOF marker sits after its APPn
    /// segments, which carry EXIF and colour profiles and can be large; 64 KB covers any ordinary
    /// photograph's, and a logo whose dimensions are further in than that is refused rather than
    /// read at unbounded cost.</summary>
    private const int MaxHeaderBytes = 64 * 1024;

    /// <summary>
    /// Reads <paramref name="content"/>'s header, or returns null when the bytes are not one of the
    /// three formats. Does not dispose or rewind the stream; callers rewind before storing.
    /// </summary>
    public static ImageHeaderInfo? TryRead(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var buffer = new byte[MaxHeaderBytes];
        var length = ReadAtMost(content, buffer);
        var bytes = buffer.AsSpan(0, length);

        return ReadPng(bytes) ?? ReadGif(bytes) ?? ReadJpeg(bytes);
    }

    private static int ReadAtMost(Stream content, byte[] buffer)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = content.Read(buffer, total, buffer.Length - total);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>PNG: an 8-byte signature, then an IHDR chunk whose width and height are big-endian
    /// 32-bit values at fixed offsets 16 and 20.</summary>
    private static ImageHeaderInfo? ReadPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature)
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        return new ImageHeaderInfo(ImageFormat.Png, BigEndian(bytes.Slice(16, 4)), BigEndian(bytes.Slice(20, 4)));
    }

    /// <summary>GIF: "GIF87a"/"GIF89a", then the logical screen width and height as little-endian
    /// 16-bit values at offsets 6 and 8.</summary>
    private static ImageHeaderInfo? ReadGif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10
            || (!bytes[..6].SequenceEqual("GIF87a"u8) && !bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return null;
        }

        return new ImageHeaderInfo(ImageFormat.Gif, bytes[6] | (bytes[7] << 8), bytes[8] | (bytes[9] << 8));
    }

    /// <summary>
    /// JPEG: a marker chain from the SOI, skipping each segment by its own length until a Start Of
    /// Frame, which carries height then width as big-endian 16-bit values. The two marker ranges
    /// excluded from the SOF family are the restart markers and the ones that carry no frame
    /// geometry (DHT, JPG, DAC) — reading one of those as a frame header is the classic way this
    /// parser gets written wrong.
    /// </summary>
    private static ImageHeaderInfo? ReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var i = 2;

        while (i + 3 < bytes.Length)
        {
            if (bytes[i] != 0xFF)
            {
                return null;
            }

            var marker = bytes[i + 1];

            // Padding fill bytes between segments are legal and are simply more 0xFF.
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // Standalone markers carry no length field.
            if (marker is 0x01 or >= 0xD0 and <= 0xD9)
            {
                i += 2;
                continue;
            }

            var segmentLength = (bytes[i + 2] << 8) | bytes[i + 3];

            if (segmentLength < 2)
            {
                return null;
            }

            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

            if (isStartOfFrame)
            {
                // SOF payload: precision (1), height (2), width (2).
                if (i + 9 >= bytes.Length)
                {
                    return null;
                }

                var height = (bytes[i + 5] << 8) | bytes[i + 6];
                var width = (bytes[i + 7] << 8) | bytes[i + 8];

                return new ImageHeaderInfo(ImageFormat.Jpeg, width, height);
            }

            i += 2 + segmentLength;
        }

        return null;
    }

    private static int BigEndian(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
