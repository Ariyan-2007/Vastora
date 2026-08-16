using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Reviews;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice review moderation — §9.25. Deleting a customer's review is Admin-tier, per §9.3's destructive-action rule.</summary>
[Tags("BackOffice - Reviews")]
[Route("api/businesses/{businessId}/reviews")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class ReviewsController(ICurrentUserContext currentUser, IReviewService reviewService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> GetAll(
        string businessId, [FromQuery] ReviewStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await reviewService.GetAllAsync(businessId, status, PageRequest.Of(page, pageSize), ct));

    [HttpPatch("{reviewId}/status")]
    public async Task<ActionResult<ReviewResponse>> Moderate(string businessId, string reviewId, ModerateReviewRequest request, CancellationToken ct) =>
        Ok(await reviewService.ModerateAsync(ResolvedTenantId, businessId, reviewId, request, ct));

    [HttpPost("{reviewId}/reply")]
    public async Task<ActionResult<ReviewResponse>> Reply(string businessId, string reviewId, MerchantReplyRequest request, CancellationToken ct) =>
        Ok(await reviewService.ReplyAsync(ResolvedTenantId, businessId, reviewId, request, ct));

    [HttpDelete("{reviewId}")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<IActionResult> Delete(string businessId, string reviewId, CancellationToken ct)
    {
        await reviewService.DeleteAsync(ResolvedTenantId, businessId, reviewId, ct);
        return NoContent();
    }
}
