using Vastora.Domain.Enums;

namespace Vastora.Application.Auth;

public record UserSummaryResponse(
    string Id,
    string FullName,
    string Email,
    UserRole Role,
    string TenantId,
    string BusinessId,
    UserStatus Status);

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
