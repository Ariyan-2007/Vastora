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
}
