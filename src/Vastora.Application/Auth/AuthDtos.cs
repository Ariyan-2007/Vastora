using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Auth;

public record UserSummaryResponse(
    string Id,
    string FullName,
    string Email,
    string Phone,
    string AvatarUrl,
    UserRole Role,
    string TenantId,
    string BusinessId,
    UserStatus Status,
    DateTime? EmailVerifiedAt,
    DateTime? PhoneVerifiedAt,
    DateTime CreatedAt)
{
    public static UserSummaryResponse From(AppUser u) => new(
        u.Id, u.FullName, u.Email, u.Phone, u.AvatarUrl, u.Role, u.TenantId, u.BusinessId, u.Status,
        u.EmailVerifiedAt, u.PhoneVerifiedAt, u.CreatedAt);
}

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummaryResponse User);

public record BackOfficeLoginRequest(string Email, string Password);

public record StorefrontLoginRequest(string Email, string Password);

public record StorefrontRegisterRequest(string FullName, string Email, string Password, string Phone);

public record RefreshTokenRequest(string RefreshToken);

/// <summary>Always 204s regardless of whether the email matches an account — avoids user enumeration (§9.10).</summary>
public record ForgotPasswordRequest(string Email);

/// <summary>Shared by every realm — the token itself already identifies which user/account it belongs to.</summary>
public record ResetPasswordRequest(string Token, string NewPassword);

/// <summary>§9.34. Shared by every realm — the token identifies the account, same as password reset.</summary>
public record VerifyEmailRequest(string Token);

/// <summary>Authenticated self-service change — distinct from the token-based forgot/reset flow, which is for a locked-out user with no session.</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
