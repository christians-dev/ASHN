using System.Text;

using Ashen.Catalog.Images;

namespace Ashen.UnitTests.Catalog;

public class ImageValidatorTests
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    public static readonly byte[] WebP = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBP"u8];

    public static TheoryData<byte[], string> Images => new()
    {
        { Png, ".png" },
        { Jpeg, ".jpg" },
        { WebP, ".webp" },
    };

    [Theory]
    [MemberData(nameof(Images))]
    public void Accepts_jpeg_png_and_webp_by_their_content(byte[] header, string extension)
    {
        var result = ImageValidator.Check(header, header.Length);

        Assert.True(result.IsSuccess);
        Assert.Equal(extension, result.Value);
    }

    [Fact]
    public void Rejects_a_text_file_renamed_to_png()
    {
        var text = Encoding.UTF8.GetBytes("<svg onload=alert(1)>");

        Assert.False(ImageValidator.Check(text, text.Length).IsSuccess);
    }

    [Fact]
    public void Rejects_files_over_10_mb()
    {
        Assert.False(ImageValidator.Check(Png, ImageValidator.MaxBytes + 1).IsSuccess);
        Assert.True(ImageValidator.Check(Png, ImageValidator.MaxBytes).IsSuccess);
    }

    [Fact]
    public void Rejects_empty_files()
    {
        Assert.False(ImageValidator.Check([], 0).IsSuccess);
    }
}
