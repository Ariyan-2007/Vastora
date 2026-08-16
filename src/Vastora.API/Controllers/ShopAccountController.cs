using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.GiftCards;
using Vastora.Application.Privacy;
using Vastora.Application.Reviews;
using Vastora.Application.Wishlists;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// A customer's own account surface: wishlist, reviews they've written, store credit, gift-card
/// balances, notification preferences and their data-rights actions — §9.24, §9.25, §9.26, §9.37.
/// Scope comes entirely from the JWT, never the route.
/// </summary>
[Tags("Shop - Account")]
[Route("api/shop/account")]
[Authorize(Roles = nameof(UserRole.Customer))]
public class ShopAccountController(
    ICurrentUserContext currentUser,
    IWishlistService wishlistService,
    IReviewService reviewService,
    IStoreCreditService storeCreditService,
    IGiftCardService giftCardService,
    IPrivacyService privacyService) : VastoraControllerBase(currentUser)
{
    // --- §9.26: wishlist ---

    [HttpGet("wishlist")]
    public async Task<ActionResult<PagedResult<WishlistItemResponse>>> GetWishlist(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await wishlistService.GetAsync(CurrentUser.BusinessId, CurrentUser.UserId, PageRequest.Of(page, pageSize), ct));

    [HttpPost("wishlist/{productId}")]
    public async Task<ActionResult<WishlistItemResponse>> AddToWishlist(string productId, CancellationToken ct) =>
        Ok(await wishlistService.AddAsync(CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, productId, ct));

    [HttpDelete("wishlist/{productId}")]
    public async Task<IActionResult> RemoveFromWishlist(string productId, CancellationToken ct)
    {
        await wishlistService.RemoveAsync(CurrentUser.BusinessId, CurrentUser.UserId, productId, ct);
        return NoContent();
    }

    // --- §9.25: writing a review ---

    [HttpPost("reviews")]
    public async Task<ActionResult<ReviewResponse>> SubmitReview(CreateReviewRequest request, CancellationToken ct) =>
        Ok(await reviewService.SubmitAsync(CurrentUser.TenantId, CurrentUser.BusinessId, CurrentUser.UserId, request, ct));

    // --- §9.24: balances ---

    [HttpGet("store-credit")]
    public async Task<ActionResult<StoreCreditBalanceResponse>> GetStoreCredit(CancellationToken ct) =>
        Ok(await storeCreditService.GetStatementAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct));

    [HttpGet("gift-cards/{code}")]
    public async Task<ActionResult<GiftCardBalanceResponse>> CheckGiftCard(string code, CancellationToken ct) =>
        Ok(await giftCardService.CheckBalanceAsync(CurrentUser.BusinessId, code, ct));

    // --- §9.37: data rights ---

    [HttpGet("data-export")]
    public async Task<ActionResult<CustomerDataExport>> ExportMyData(CancellationToken ct) =>
        Ok(await privacyService.ExportCustomerDataAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct));

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> UpdatePreferences(UpdateNotificationPreferencesRequest request, CancellationToken ct)
    {
        await privacyService.UpdatePreferencesAsync(CurrentUser.UserId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Right to erasure. Irreversible, and it signs the account out permanently — the PII is
    /// overwritten rather than the record deleted, because orders reference it and the merchant
    /// has its own legal duty to retain those.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteMyAccount(CancellationToken ct)
    {
        await privacyService.AnonymizeCustomerAsync(CurrentUser.BusinessId, CurrentUser.UserId, ct);
        return NoContent();
    }
}
