using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Auth;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Privacy;
using Vastora.Application.Users;

namespace Vastora.API.Controllers;

/// <summary>Login realm for every non-customer role: PlatformSuperAdmin, TenantOwner, BusinessAdmin, BusinessStaff, DeliveryAgent.</summary>
[Tags("Auth")]
[Route("api/auth")]
public class AuthController(
    ICurrentUserContext currentUser,
    IAuthService authService,
    IUserService userService,
    IPrivacyService privacyService,
    IFileStorageService fileStorage) : VastoraControllerBase(currentUser)
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

    /// <summary>
    /// §9.34. Confirms an email address. Public because the holder of the token may not have a
    /// session — for a PendingVerification account they cannot have one, which is the point.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        await authService.VerifyEmailAsync(request.Token, ct);
        return NoContent();
    }

    [HttpPost("resend-verification")]
    [Authorize]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        await authService.RequestEmailVerificationAsync(CurrentUser.UserId, ct);
        return NoContent();
    }

    /// <summary>
    /// §9.36/§9.37. One-click unsubscribe by token — no login required, because a marketing
    /// recipient may not have (or remember) an account session, and an opt-out that demands a
    /// password is not really an opt-out.
    /// </summary>
    [HttpPost("unsubscribe/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe(string token, CancellationToken ct)
    {
        // Always 204, whether or not the token matched — same non-enumeration reasoning as
        // forgot-password (§9.10).
        await privacyService.UnsubscribeByTokenAsync(token, ct);
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

    /// <summary>Authenticated self-service change — distinct from the token-based forgot/reset flow (§4). Revokes every active session on success.</summary>
    [HttpPost("me/change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await authService.ChangePasswordAsync(CurrentUser.UserId, request.CurrentPassword, request.NewPassword, ct);
        return NoContent();
    }

    /// <summary>Local disk storage — see IFileStorageService. Same content-type whitelist/size limit as product images.</summary>
    [HttpPost("me/avatar")]
    [Authorize]
    [RequestSizeLimit(ImageUploadPolicy.MaxFileSizeBytes)]
    public async Task<ActionResult<UserSummaryResponse>> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return BadRequest("File is empty.");
        }

        if (!ImageUploadPolicy.TryGetExtension(file.ContentType, out var extension))
        {
            return BadRequest(ImageUploadPolicy.UnsupportedTypeMessage);
        }

        await using var stream = file.OpenReadStream();
        // Files are grouped by Business for storage; a Tenant-/Platform-level account (no single
        // Business) falls back to a shared bucket rather than failing to upload at all.
        var storageScope = string.IsNullOrEmpty(CurrentUser.BusinessId) ? "platform" : CurrentUser.BusinessId;
        var url = await fileStorage.SaveAsync(storageScope, stream, extension, ct);

        var result = await userService.UpdateAvatarAsync(CurrentUser.UserId, url, ct);
        return Ok(result);
    }

    [HttpDelete("me/avatar")]
    [Authorize]
    public async Task<ActionResult<UserSummaryResponse>> RemoveAvatar(CancellationToken ct)
    {
        var result = await userService.RemoveAvatarAsync(CurrentUser.UserId, ct);
        return Ok(result);
    }

    private string RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
