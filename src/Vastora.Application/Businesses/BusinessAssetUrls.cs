using Vastora.Domain.Entities;

namespace Vastora.Application.Businesses;

/// <summary>
/// Resolves a Business's relative <c>LogoUrl</c> (as stored — see
/// <c>LocalFileStorageService.SaveAsync</c>, deliberately host-relative) to an absolute URL, for
/// the one class of consumer with no implicit base to resolve a relative path against: outbound
/// email (<c>EmailTemplates.Layout</c>). Every other consumer — a BackOffice/SuperOffice/Shop
/// frontend rendering an &lt;img&gt; in a browser — already has its own base URL to resolve
/// against and needs no help here.
///
/// Prefers the Business's own <see cref="Business.ShopDomain"/> (SuperOffice-set) over the
/// platform's own base, so a Business's mail keeps rendering correctly off its own address even
/// when the platform's happens to be something else (a dev tunnel, a shared platform domain).
/// </summary>
public static class BusinessAssetUrls
{
    /// <summary>
    /// Mutates and returns the same instance. Safe: every caller fetches <paramref name="business"/>
    /// fresh from the repository (MongoDB deserializes a new object per query — no shared/cached
    /// reference) solely to build one outbound message, never to persist or return it afterward.
    /// </summary>
    public static Business? ResolveLogo(Business? business, string platformBaseUrl)
    {
        if (business is null || string.IsNullOrWhiteSpace(business.LogoUrl))
        {
            return business;
        }

        business.LogoUrl = Resolve(business.LogoUrl, business.ShopDomain, platformBaseUrl);
        return business;
    }

    private static string Resolve(string url, string? shopDomain, string platformBaseUrl)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Already absolute — either resolved already, or a business owner pasted an external
            // URL (their own CDN, say) directly into LogoUrl. Either way, leave it alone.
            return url;
        }

        var baseUrl = string.IsNullOrWhiteSpace(shopDomain) ? platformBaseUrl : NormalizeScheme(shopDomain);
        return baseUrl.TrimEnd('/') + url;
    }

    private static string NormalizeScheme(string domain) =>
        domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? domain
            : "https://" + domain;
}
