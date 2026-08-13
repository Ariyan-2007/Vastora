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
