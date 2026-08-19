namespace Vastora.Application.Common.Interfaces;

/// <summary>
/// Deployment-level switches the Application layer needs to read. An interface rather than an
/// injected IConfiguration so Application keeps depending only on abstractions — the same reason
/// IFileStorageService and INotificationService exist.
/// </summary>
public interface IPlatformSettings
{
    /// <summary>
    /// §9.34. When true, a customer must confirm their email before they can sign in.
    ///
    /// Defaults to **false**, and that is a deliberate compromise rather than an oversight: the
    /// only INotificationService implementation logs instead of sending (§9.10), so defaulting
    /// this on would lock every new customer out of every shop until an operator read the server
    /// log. Turn it on in the same change that wires a real email provider — the switch exists so
    /// that is a config edit, not a code change.
    /// </summary>
    bool RequireEmailVerification { get; }

    /// <summary>§9.36. How long a cart sits untouched before it counts as abandoned.</summary>
    TimeSpan AbandonedCartAfter { get; }

    /// <summary>
    /// The <em>frontend's</em> base URL — Shop/BackOffice/SuperOffice, whichever app actually
    /// renders the page a link points at. Used as the last-resort default when building a
    /// customer/staff-facing page link (email verification, password reset, unsubscribe) —
    /// callers that know their own origin (e.g. <c>ForgotPasswordRequest.RedirectBaseUrl</c>,
    /// sent as <c>window.location.origin</c>) should prefer that instead, once
    /// <c>AuthService.ResolveLinkBase</c> has validated it against a known-legitimate origin for
    /// the request's realm. This value alone is never enough for a platform serving several
    /// distinct frontends off one backend — it's one fixed address, not "whichever site the
    /// customer is actually on" — which is exactly the gap <see cref="AllowedFrontendOrigins"/>,
    /// <c>Business.ShopDomain</c>/<c>BackOfficeDomain</c>, and <c>TenantAccount.SuperOfficeDomain</c>
    /// close, each dynamically settable through its own API rather than requiring a config edit.
    /// </summary>
    string PublicBaseUrl { get; }

    /// <summary>
    /// Known-legitimate origins for the PlatformSuperAdmin realm only — <c>Platform:AllowedFrontendOrigins</c>,
    /// same shape and reasoning as <c>Cors:AllowedOrigins</c>. Every other realm now has its own
    /// dynamically API-settable trust anchor instead (<c>Business.BackOfficeDomain</c> for
    /// BusinessAdmin/BusinessStaff/DeliveryAgent, <c>Business.ShopDomain</c> for Customer,
    /// <c>TenantAccount.SuperOfficeDomain</c> for TenantOwner); this one stays static config
    /// because PlatformSuperAdmin has no Business or Tenant above it, and no one but Platform's
    /// own config is positioned to set it. A request-supplied <c>RedirectBaseUrl</c> is only ever
    /// honored if it exactly matches an entry here; otherwise <see cref="PublicBaseUrl"/> is used
    /// instead. Empty by default.
    /// </summary>
    IReadOnlyList<string> AllowedFrontendOrigins { get; }

    /// <summary>
    /// This API's <em>own</em> reachable address — used only to resolve a Business's relative
    /// <c>LogoUrl</c> (as stored — see <c>LocalFileStorageService</c>) to an absolute URL for
    /// outbound email (<c>BusinessAssetUrls</c>), since that's the one asset actually served by
    /// this API itself (<c>/uploads/...</c>), unlike the frontend pages <see cref="PublicBaseUrl"/>
    /// points at. Prefers the live request's own scheme+host when one is in flight — self-
    /// correcting across a dev tunnel, staging, and production with no config needed — falling
    /// back to <c>Platform:ApiBaseUrl</c> only for the one code path with no request to read:
    /// <c>LifecycleNotificationWorker</c>'s background sweep.
    /// </summary>
    string ApiBaseUrl { get; }
}
