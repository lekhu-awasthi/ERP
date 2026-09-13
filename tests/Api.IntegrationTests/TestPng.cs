using System.IO.Compression;

namespace ErpApp.Api.IntegrationTests;

/// <summary>
/// Phase 39 — builds a real, decodable PNG of a given size.
///
/// <para><b>Why a generator and not a checked-in fixture.</b> The logo path has two consumers with
/// opposite needs: <c>ImageHeader</c> must read dimensions out of the header, and QuestPDF must
/// actually decode the pixels to draw it. A hand-built header satisfies the first and fails the
/// second, and a checked-in binary satisfies both but can only ever be one size — while the rules
/// under test are about sizes (300×300 is the floor, and the test that matters is the one just
/// under it). Generating covers both and lets a test name its own dimensions.</para>
///
/// <para>It is a genuine PNG: IHDR, a zlib-wrapped IDAT of filtered scanlines, IEND, with a real
/// CRC on every chunk. Roughly fifty lines against an imaging dependency the solution does not have
/// and does not need.</para>
/// </summary>
public static class TestPng
{
    public static byte[] Create(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        // IHDR: 8-bit RGB, no interlace.
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        WriteChunk(output, "IHDR"u8, ihdr);

        // One filter byte (0 = None) then three bytes per pixel, per scanline.
        var raw = new byte[height * (1 + (width * 3))];

        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 3));
            raw[row] = 0;

            for (var x = 0; x < width; x++)
            {
                var pixel = row + 1 + (x * 3);
                raw[pixel] = (byte)(x % 256);
                raw[pixel + 1] = (byte)(y % 256);
                raw[pixel + 2] = 0x80;
            }
        }

        using var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, (int)crc);
        output.Write(crcBytes);
    }

    private static void WriteBigEndian(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in type)
        {
            crc = Step(crc, b);
        }

        foreach (var b in data)
        {
            crc = Step(crc, b);
        }

        return crc ^ 0xFFFFFFFFu;

        static uint Step(uint crc, byte b)
        {
            crc ^= b;

            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }

            return crc;
        }
    }
}
