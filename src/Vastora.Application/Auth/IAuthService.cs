namespace Vastora.Application.Auth;

public interface IAuthService
{
    /// <summary>Login realm for every non-customer role: PlatformSuperAdmin, TenantOwner, BusinessAdmin, BusinessStaff, DeliveryAgent.</summary>
    Task<AuthResponse> BackOfficeLoginAsync(BackOfficeLoginRequest request, string ip, CancellationToken ct = default);

    /// <summary>Storefront login, scoped to one Business by slug.</summary>
    Task<AuthResponse> StorefrontLoginAsync(string businessSlug, StorefrontLoginRequest request, string ip, CancellationToken ct = default);

    /// <summary>Self-serve customer sign-up, scoped to one Business by slug.</summary>
    Task<AuthResponse> StorefrontRegisterAsync(string businessSlug, StorefrontRegisterRequest request, string ip, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, string ip, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// BackOffice/SuperOffice/Platform realm — always succeeds, doesn't reveal whether the email
    /// exists (§9.10). <paramref name="redirectBaseUrl"/> is the caller's own origin (e.g.
    /// <c>window.location.origin</c>) — honored only if it matches the trust anchor for that
    /// user's own realm: <c>Business.BackOfficeDomain</c> for BusinessAdmin/BusinessStaff/
    /// DeliveryAgent, <c>TenantAccount.SuperOfficeDomain</c> for TenantOwner, or
    /// <c>Platform:AllowedFrontendOrigins</c> for PlatformSuperAdmin only. Never trusted outright
    /// (see AuthService.ResolveLinkBase / ResolveStaffRealmAsync).
    /// </summary>
    Task RequestPasswordResetAsync(string email, string? redirectBaseUrl = null, CancellationToken ct = default);

    /// <summary>
    /// Shop realm, scoped to one Business's Customers by slug — same non-enumeration behavior
    /// (§9.10). <paramref name="redirectBaseUrl"/> is honored only if it matches that Business's
    /// own <c>ShopDomain</c> (see AuthService.ResolveLinkBase).
    /// </summary>
    Task RequestStorefrontPasswordResetAsync(string businessSlug, string email, string? redirectBaseUrl = null, CancellationToken ct = default);

    /// <summary>Shared by every realm — the token itself identifies the account (§9.10).</summary>
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default);

    /// <summary>§9.34. Issues (or re-issues) an email verification token, retiring any outstanding one.</summary>
    Task RequestEmailVerificationAsync(string userId, CancellationToken ct = default);

    /// <summary>§9.34. Shared by every realm — the token identifies the account, same as password reset.</summary>
    Task VerifyEmailAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Authenticated self-service change (current password required) — every realm, not just Shop.
    /// Same "something changed" signal as a token-based reset: every active session is revoked,
    /// forcing re-login everywhere including the device that made the change.
    /// </summary>
    Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken ct = default);
}
