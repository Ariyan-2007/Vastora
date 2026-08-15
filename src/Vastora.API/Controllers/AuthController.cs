using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Auth;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Users;

namespace Vastora.API.Controllers;

/// <summary>Login realm for every non-customer role: PlatformSuperAdmin, TenantOwner, BusinessAdmin, BusinessStaff, DeliveryAgent.</summary>
[Tags("Auth")]
[Route("api/auth")]
public class AuthController(ICurrentUserContext currentUser, IAuthService authService, IUserService userService)
    : VastoraControllerBase(currentUser)
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(BackOfficeLoginRequest request, CancellationToken ct)
    {
        var result = await authService.BackOfficeLoginAsync(request, RemoteIp, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, RemoteIp, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    /// <summary>Always 204s, whether or not the email matches an account (§9.10) — don't build UI that distinguishes the two.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await authService.RequestPasswordResetAsync(request.Email, ct);
        return NoContent();
    }

    /// <summary>Shared across every realm (BackOffice/SuperOffice/Platform/Shop) — the token identifies the account.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await authService.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserSummaryResponse>> Me(CancellationToken ct)
    {
        var result = await userService.GetMeAsync(CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<UserSummaryResponse>> UpdateMe(UpdateProfileRequest request, CancellationToken ct)
    {
        var result = await userService.UpdateProfileAsync(CurrentUser.UserId, request, ct);
        return Ok(result);
    }

    private string RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
