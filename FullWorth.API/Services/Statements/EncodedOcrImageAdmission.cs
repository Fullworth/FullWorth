using System.Buffers.Binary;

namespace FullWorth.API.Services.Statements;

/// <summary>
/// Reads only bounded PNG/JPEG headers before any native image decoder runs and
/// applies the shared conservative OCR working-set admission ceiling.
/// This is defense in depth; the in-process native OCR path still requires
/// separate-process isolation for a hard operating-system memory limit.
/// </summary>
internal static class EncodedOcrImageAdmission
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    internal static bool TryAdmit(
        ReadOnlySpan<byte> encodedImage,
        string mediaType,
        out long pixelCount)
    {
        pixelCount = 0;

        if (encodedImage.IsEmpty ||
            string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        var hasDimensions =
            string.Equals(
                mediaType,
                "image/png",
                StringComparison.OrdinalIgnoreCase)
                ? TryReadPngPixelCount(
                    encodedImage,
                    out pixelCount)
                : string.Equals(
                    mediaType,
                    "image/jpeg",
                    StringComparison.OrdinalIgnoreCase) &&
                  TryReadJpegPixelCount(
                      encodedImage,
                      out pixelCount);

        if (!hasDimensions)
        {
            pixelCount = 0;
            return false;
        }

        if (!PdfImageMemoryAdmission.TryEstimate(
                pixelCount,
                encodedImage.Length,
                out _))
        {
            pixelCount = 0;
            return false;
        }

        return true;
    }

    private static bool TryReadPngPixelCount(
        ReadOnlySpan<byte> encodedImage,
        out long pixelCount)
    {
        pixelCount = 0;

        if (encodedImage.Length < 24 ||
            !encodedImage[..8].SequenceEqual(PngSignature) ||
            BinaryPrimitives.ReadUInt32BigEndian(
                encodedImage.Slice(8, 4)) != 13 ||
            !encodedImage.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width =
            BinaryPrimitives.ReadUInt32BigEndian(
                encodedImage.Slice(16, 4));
        var height =
            BinaryPrimitives.ReadUInt32BigEndian(
                encodedImage.Slice(20, 4));

        return TryCalculatePixelCount(
            width,
            height,
            out pixelCount);
    }

    private static bool TryReadJpegPixelCount(
        ReadOnlySpan<byte> encodedImage,
        out long pixelCount)
    {
        pixelCount = 0;

        if (encodedImage.Length < 4 ||
            encodedImage[0] != 0xFF ||
            encodedImage[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset < encodedImage.Length)
        {
            while (offset < encodedImage.Length &&
                   encodedImage[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= encodedImage.Length)
            {
                return false;
            }

            var marker = encodedImage[offset++];
            if (marker == 0x00)
            {
                return false;
            }

            if (marker is 0xD8 or 0x01 ||
                marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (marker is 0xD9 or 0xDA ||
                offset > encodedImage.Length - 2)
            {
                return false;
            }

            var segmentLength =
                BinaryPrimitives.ReadUInt16BigEndian(
                    encodedImage.Slice(offset, 2));
            if (segmentLength < 2 ||
                segmentLength > encodedImage.Length - offset)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    return false;
                }

                var height =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        encodedImage.Slice(offset + 3, 2));
                var width =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        encodedImage.Slice(offset + 5, 2));

                return TryCalculatePixelCount(
                    width,
                    height,
                    out pixelCount);
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or
            0xCD or 0xCE or 0xCF;

    private static bool TryCalculatePixelCount(
        ulong width,
        ulong height,
        out long pixelCount)
    {
        pixelCount = 0;

        if (width == 0 ||
            height == 0 ||
            width > (ulong)long.MaxValue / height)
        {
            return false;
        }

        var pixels = width * height;
        if (pixels > long.MaxValue)
        {
            return false;
        }

        pixelCount = (long)pixels;
        return true;
    }
}
