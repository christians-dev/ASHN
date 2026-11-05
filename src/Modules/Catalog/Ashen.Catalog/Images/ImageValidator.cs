using Ashen.BuildingBlocks;

namespace Ashen.Catalog.Images;

/// <summary>Accepts JPEG, PNG or WebP up to 10 MB, judged by the file's bytes, never its name (FR-043).</summary>
internal static class ImageValidator
{
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>How many leading bytes <see cref="Check"/> needs.</summary>
    public const int HeaderLength = 12;

    /// <returns>The file extension to store the image under.</returns>
    public static Result<string> Check(ReadOnlySpan<byte> header, long length)
    {
        if (length is <= 0 or > MaxBytes)
        {
            return Error.Validation("image.size", "Images must be between 1 byte and 10 MB.");
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return ".jpg";
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        if (header.Length >= HeaderLength && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return Error.Validation("image.type", "Only JPEG, PNG and WebP images are accepted.");
    }
}
