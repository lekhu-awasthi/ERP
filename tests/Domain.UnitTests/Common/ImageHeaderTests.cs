using ErpApp.Domain.Common;

namespace ErpApp.Domain.UnitTests.Common;

/// <summary>
/// Phase 39. <see cref="ImageHeader"/> is what stands between a multipart upload and an image this
/// app embeds in customer-facing PDFs and serves back to browsers, so it is tested as a
/// <b>format check</b> first: the cases that matter most are the ones where the bytes and the
/// filename disagree.
/// </summary>
public class ImageHeaderTests
{
    // --- the three formats --------------------------------------------------------------------

    [Fact]
    public void Reads_a_png_signature_and_its_IHDR_dimensions()
    {
        var info = ImageHeader.TryRead(new MemoryStream(Png(1280, 720)));

        Assert.NotNull(info);
        Assert.Equal(ImageFormat.Png, info.Format);
        Assert.Equal(1280, info.Width);
        Assert.Equal(720, info.Height);
        Assert.Equal("image/png", info.ContentType);
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Reads_both_gif_versions_and_their_little_endian_dimensions(string version)
    {
        var info = ImageHeader.TryRead(new MemoryStream(Gif(version, 400, 300)));

        Assert.NotNull(info);
        Assert.Equal(ImageFormat.Gif, info.Format);
        Assert.Equal(400, info.Width);
        Assert.Equal(300, info.Height);
    }

    [Fact]
    public void Reads_a_jpeg_by_walking_its_marker_chain_past_the_app_segments()
    {
        // An APP0/JFIF segment and a 2 KB APP1/EXIF block before the frame header, which is what a
        // photograph straight off a phone looks like and what a naive "read bytes 4..10" parser
        // gets wrong.
        var info = ImageHeader.TryRead(new MemoryStream(Jpeg(640, 480, appSegmentBytes: 2048)));

        Assert.NotNull(info);
        Assert.Equal(ImageFormat.Jpeg, info.Format);
        Assert.Equal(640, info.Width);
        Assert.Equal(480, info.Height);
        Assert.Equal("image/jpeg", info.ContentType);
    }

    /// <summary>A progressive JPEG's frame marker is SOF2, not SOF0. Both are in the SOF family and
    /// both carry geometry; a parser that looked only for 0xC0 would reject half the web's
    /// photographs.</summary>
    [Fact]
    public void Reads_a_progressive_jpeg_whose_frame_marker_is_SOF2()
    {
        var info = ImageHeader.TryRead(new MemoryStream(Jpeg(200, 100, appSegmentBytes: 16, sofMarker: 0xC2)));

        Assert.NotNull(info);
        Assert.Equal(200, info.Width);
    }

    /// <summary>DHT (0xC4) sits inside the 0xC0–0xCF range and is <i>not</i> a frame header. Reading
    /// one as a frame is the classic way this parser is written wrong, and it yields confident
    /// nonsense rather than a failure.</summary>
    [Fact]
    public void Does_not_mistake_a_huffman_table_for_a_frame_header()
    {
        var info = ImageHeader.TryRead(new MemoryStream(JpegWithHuffmanTableBeforeFrame(800, 600)));

        Assert.NotNull(info);
        Assert.Equal(800, info.Width);
        Assert.Equal(600, info.Height);
    }

    // --- what it refuses ----------------------------------------------------------------------

    /// <summary>
    /// The case the whole class exists for: a file whose name and declared content type say PNG and
    /// whose bytes say something else. The upload command reads the bytes, so this is what it sees.
    /// </summary>
    [Theory]
    [InlineData("<?php system($_GET['c']); ?>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("%PDF-1.7")]
    [InlineData("GIF8")]
    [InlineData("")]
    public void Refuses_bytes_that_are_not_one_of_the_three_formats(string content)
    {
        Assert.Null(ImageHeader.TryRead(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(content))));
    }

    /// <summary>A truncated PNG has the right signature and not enough bytes to state a size.
    /// Returning null beats returning a zero-by-zero image the size check would then reject with a
    /// misleading message.</summary>
    [Fact]
    public void Refuses_a_png_truncated_before_its_dimensions()
    {
        Assert.Null(ImageHeader.TryRead(new MemoryStream(Png(100, 100)[..16])));
    }

    /// <summary>A JPEG whose marker chain never reaches a frame header — a header-only file, or one
    /// deliberately padded past the read window — is refused rather than scanned indefinitely.</summary>
    [Fact]
    public void Refuses_a_jpeg_with_no_frame_header()
    {
        Assert.Null(ImageHeader.TryRead(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00])));
    }

    /// <summary>The stream is read forward only and is not rewound — the caller does that before
    /// storing, and this pins the contract so a caller cannot come to rely on the opposite.</summary>
    [Fact]
    public void Does_not_rewind_the_stream_it_was_given()
    {
        var stream = new MemoryStream(Png(300, 300));

        ImageHeader.TryRead(stream);

        Assert.True(stream.Position > 0);
    }

    // --- builders -----------------------------------------------------------------------------

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BigEndian(bytes, 16, width);
        BigEndian(bytes, 20, height);

        return bytes;
    }

    private static byte[] Gif(string version, int width, int height)
    {
        var bytes = new byte[13];
        System.Text.Encoding.ASCII.GetBytes(version).CopyTo(bytes, 0);
        bytes[6] = (byte)(width & 0xFF);
        bytes[7] = (byte)(width >> 8);
        bytes[8] = (byte)(height & 0xFF);
        bytes[9] = (byte)(height >> 8);

        return bytes;
    }

    private static byte[] Jpeg(int width, int height, int appSegmentBytes, byte sofMarker = 0xC0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        // APP1 with a payload of the requested size, to push the frame header well past the front.
        bytes.AddRange([0xFF, 0xE1, (byte)((appSegmentBytes + 2) >> 8), (byte)((appSegmentBytes + 2) & 0xFF)]);
        bytes.AddRange(new byte[appSegmentBytes]);

        bytes.AddRange([0xFF, sofMarker, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        bytes.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);
        bytes.AddRange(new byte[10]);

        return [.. bytes];
    }

    private static byte[] JpegWithHuffmanTableBeforeFrame(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC4, 0x00, 0x0A };
        bytes.AddRange(new byte[8]);
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        bytes.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);
        bytes.AddRange(new byte[10]);

        return [.. bytes];
    }

    private static void BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
