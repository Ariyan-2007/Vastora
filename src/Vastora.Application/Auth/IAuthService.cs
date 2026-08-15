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

    /// <summary>BackOffice/SuperOffice/Platform realm — always succeeds, doesn't reveal whether the email exists (§9.10).</summary>
    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);

    /// <summary>Shop realm, scoped to one Business's Customers by slug — same non-enumeration behavior (§9.10).</summary>
    Task RequestStorefrontPasswordResetAsync(string businessSlug, string email, CancellationToken ct = default);

    /// <summary>Shared by every realm — the token itself identifies the account (§9.10).</summary>
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default);
}
