namespace Vastora.Application.Common;

/// <summary>
/// Shared whitelist for every controller that accepts an image upload (products, avatars,
/// categories, business logo/banner, content blocks) — one place instead of each controller
/// hand-copying its own content-type dictionary.
/// </summary>
public static class ImageUploadPolicy
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public const string UnsupportedTypeMessage = "Unsupported image type. Allowed: image/jpeg, image/png, image/webp, image/gif.";

    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    public static bool TryGetExtension(string contentType, out string extension) =>
        AllowedContentTypes.TryGetValue(contentType, out extension!);
}
