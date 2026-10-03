using System.Buffers.Binary;
using System.Text;

namespace FullWorth.Tests.Services.Statements;

internal sealed record GeneratedDocumentSecurityCase(
    string Name,
    string MediaType,
    string FileExtension,
    byte[] Bytes);

internal static class GeneratedMaliciousDocumentCorpus
{
    internal static IReadOnlyList<GeneratedDocumentSecurityCase>
        PdfCases =>
        [
            Pdf(
                "pdf-header-with-random-body",
                "%PDF-1.7\nThis is not a valid PDF document."),

            Pdf(
                "pdf-catalog-without-page-tree",
                "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF"),

            Pdf(
                "pdf-xref-points-to-missing-root",
                "%PDF-1.7\nxref\n0 2\n0000000000 65535 f \n0000000000 00000 n \ntrailer\n<< /Size 2 /Root 99 0 R >>\nstartxref\n9\n%%EOF"),

            Pdf(
                "pdf-stream-declares-int-max-length",
                "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n3 0 obj\n<< /Length 2147483647 /Filter /FlateDecode >>\nstream\nx\nendstream\nendobj\n%%EOF"),

            Pdf(
                "pdf-invalid-ascii-hex-stream",
                "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n3 0 obj\n<< /Length 2 /Filter /ASCIIHexDecode >>\nstream\nGG\nendstream\nendobj\n%%EOF"),

            Pdf(
                "pdf-invalid-flate-stream",
                "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n3 0 obj\n<< /Length 4 /Filter /FlateDecode >>\nstream\nnope\nendstream\nendobj\n%%EOF")
        ];

    internal static IReadOnlyList<GeneratedDocumentSecurityCase>
        ImageCases =>
        [
            Image(
                "png-truncated-signature",
                "image/png",
                ".png",
                [0x89, 0x50, 0x4E]),

            Image(
                "png-truncated-ihdr",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100)[..24]),

            Image(
                "png-zero-width",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 0,
                    height: 100)),

            Image(
                "png-working-set-expansion",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 10_000,
                    height: 5_000)),

            Image(
                "png-invalid-bit-depth",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100,
                    bitDepth: 3,
                    colorType: 2)),

            Image(
                "png-invalid-color-type",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100,
                    colorType: 7)),

            Image(
                "png-invalid-compression-method",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100,
                    compressionMethod: 1)),

            Image(
                "png-invalid-filter-method",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100,
                    filterMethod: 1)),

            Image(
                "png-invalid-interlace-method",
                "image/png",
                ".png",
                CreatePngHeader(
                    width: 100,
                    height: 100,
                    interlaceMethod: 2)),

            Image(
                "png-invalid-ihdr-crc",
                "image/png",
                ".png",
                CorruptLastByte(
                    CreatePngHeader(
                        width: 100,
                        height: 100))),

            Image(
                "jpeg-truncated-segment",
                "image/jpeg",
                ".jpg",
                [
                    0xFF, 0xD8,
                    0xFF, 0xE0,
                    0x7F, 0xFF,
                    0x00
                ]),

            Image(
                "jpeg-scan-before-frame",
                "image/jpeg",
                ".jpg",
                [
                    0xFF, 0xD8,
                    0xFF, 0xDA,
                    0x00, 0x02
                ]),

            Image(
                "jpeg-zero-width",
                "image/jpeg",
                ".jpg",
                CreateJpegHeader(
                    width: 0,
                    height: 100)),

            Image(
                "jpeg-working-set-expansion",
                "image/jpeg",
                ".jpg",
                CreateJpegHeader(
                    width: ushort.MaxValue,
                    height: ushort.MaxValue)),

            Image(
                "jpeg-short-start-of-frame",
                "image/jpeg",
                ".jpg",
                [
                    0xFF, 0xD8,
                    0xFF, 0xC0,
                    0x00, 0x06,
                    0x08, 0x00,
                    0x64, 0x00
                ])
        ];

    private static GeneratedDocumentSecurityCase Pdf(
        string name,
        string body) =>
        new(
            name,
            "application/pdf",
            ".pdf",
            Encoding.ASCII.GetBytes(
                body));

    private static GeneratedDocumentSecurityCase Image(
        string name,
        string mediaType,
        string fileExtension,
        byte[] bytes) =>
        new(
            name,
            mediaType,
            fileExtension,
            bytes);

    private static byte[] CreatePngHeader(
        uint width,
        uint height,
        byte bitDepth = 8,
        byte colorType = 2,
        byte compressionMethod = 0,
        byte filterMethod = 0,
        byte interlaceMethod = 0)
    {
        var image =
            new byte[33];

        byte[] signature =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];

        signature.CopyTo(
            image,
            0);

        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(8, 4),
            13);

        "IHDR"u8.CopyTo(
            image.AsSpan(12, 4));

        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(16, 4),
            width);

        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(20, 4),
            height);

        image[24] = bitDepth;
        image[25] = colorType;
        image[26] = compressionMethod;
        image[27] = filterMethod;
        image[28] = interlaceMethod;

        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(29, 4),
            ComputeCrc32(
                image.AsSpan(12, 17)));

        return image;
    }

    private static byte[] CreateJpegHeader(
        ushort width,
        ushort height)
    {
        byte[] image =
        [
            0xFF, 0xD8,
            0xFF, 0xE0,
            0x00, 0x04,
            0x00, 0x00,
            0xFF, 0xC0,
            0x00, 0x07,
            0x08,
            0x00, 0x00,
            0x00, 0x00
        ];

        BinaryPrimitives.WriteUInt16BigEndian(
            image.AsSpan(13, 2),
            height);

        BinaryPrimitives.WriteUInt16BigEndian(
            image.AsSpan(15, 2),
            width);

        return image;
    }

    private static byte[] CorruptLastByte(
        byte[] value)
    {
        value[^1] ^= 0x01;
        return value;
    }

    private static uint ComputeCrc32(
        ReadOnlySpan<byte> value)
    {
        var crc =
            uint.MaxValue;

        foreach (var item in value)
        {
            crc ^=
                item;

            for (var bit = 0;
                 bit < 8;
                 bit++)
            {
                crc =
                    (crc & 1) != 0
                        ? (crc >> 1) ^ 0xEDB88320u
                        : crc >> 1;
            }
        }

        return ~crc;
    }
}
