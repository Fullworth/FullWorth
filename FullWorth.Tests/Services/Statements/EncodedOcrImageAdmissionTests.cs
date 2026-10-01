using System.Buffers.Binary;
using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services.Statements;

public sealed class EncodedOcrImageAdmissionTests
{
    [Fact]
    public void TryAdmit_AllowsBoundedPngDimensions()
    {
        var image = CreatePngHeader(
            width: 5_000,
            height: 4_000);

        Assert.True(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/png",
                out var pixelCount));
        Assert.Equal(
            20_000_000,
            pixelCount);
    }

    [Fact]
    public void TryAdmit_RejectsPngAboveWorkingSetCeiling()
    {
        var image = CreatePngHeader(
            width: 10_000,
            height: 5_000);

        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/png",
                out var pixelCount));
        Assert.Equal(
            0,
            pixelCount);
    }

    [Fact]
    public void TryAdmit_AllowsBoundedJpegDimensions()
    {
        var image = CreateJpegHeader(
            width: 4_000,
            height: 3_000);

        Assert.True(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/jpeg",
                out var pixelCount));
        Assert.Equal(
            12_000_000,
            pixelCount);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    public void TryAdmit_RejectsMalformedOrUnsupportedImages(
        string mediaType)
    {
        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                [0x01, 0x02, 0x03],
                mediaType,
                out var pixelCount));
        Assert.Equal(
            0,
            pixelCount);
    }

    [Fact]
    public void TryAdmit_RejectsZeroPngDimension()
    {
        var image = CreatePngHeader(
            width: 0,
            height: 100);

        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/png",
                out _));
    }

    [Fact]
    public void TryAdmit_RejectsJpegWithoutStartOfFrame()
    {
        byte[] image =
        [
            0xFF, 0xD8,
            0xFF, 0xE0,
            0x00, 0x04,
            0x00, 0x00,
            0xFF, 0xDA,
            0x00, 0x02
        ];

        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/jpeg",
                out _));
    }

    [Fact]
    public void TryAdmit_RejectsTruncatedJpegSegmentWithoutOverread()
    {
        byte[] image =
        [
            0xFF, 0xD8,
            0xFF, 0xE0,
            0x7F, 0xFF,
            0x00
        ];

        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                image,
                "image/jpeg",
                out _));
    }

    private static byte[] CreatePngHeader(
        uint width,
        uint height)
    {
        byte[] image = new byte[24];
        byte[] signature =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A
        ];
        signature.CopyTo(
            image,
            0);
        "IHDR"u8.CopyTo(
            image.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(16, 4),
            width);
        BinaryPrimitives.WriteUInt32BigEndian(
            image.AsSpan(20, 4),
            height);
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
}
