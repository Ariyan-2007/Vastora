using System.Text.RegularExpressions;

namespace Vastora.Application.Common;

public static partial class SlugHelper
{
    public static string Slugify(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var withDashes = NonAlphaNumeric().Replace(lowered, "-");
        var collapsed = MultipleDashes().Replace(withDashes, "-").Trim('-');
        return collapsed.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : collapsed;
    }

    public static string WithSuffix(string baseSlug, int attempt) =>
        attempt == 0 ? baseSlug : $"{baseSlug}-{attempt}";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumeric();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleDashes();
}
