using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Auth;
using Vastora.Application.Common.Interfaces;

namespace Vastora.API.Controllers;

/// <summary>Customer sign-up/login, scoped to a single Business's storefront by its public slug.</summary>
[Tags("Shop - Auth")]
[Route("api/shop/{businessSlug}/auth")]
[AllowAnonymous]
public class ShopAuthController(ICurrentUserContext currentUser, IAuthService authService) : VastoraControllerBase(currentUser)
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(string businessSlug, StorefrontRegisterRequest request, CancellationToken ct)
    {
        var result = await authService.StorefrontRegisterAsync(businessSlug, request, RemoteIp, ct);
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(string businessSlug, StorefrontLoginRequest request, CancellationToken ct)
    {
        var result = await authService.StorefrontLoginAsync(businessSlug, request, RemoteIp, ct);
        return Ok(result);
    }

    /// <summary>Always 204s, whether or not the email matches a Customer account on this Business (§9.10).</summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(string businessSlug, ForgotPasswordRequest request, CancellationToken ct)
    {
        await authService.RequestStorefrontPasswordResetAsync(businessSlug, request.Email, ct);
        return NoContent();
    }

    private string RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
