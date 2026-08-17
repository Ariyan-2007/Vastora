using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Content;
using Vastora.Application.CustomerGroups;
using Vastora.Application.GiftCards;
using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>
/// BackOffice merchandising: promotions, customer groups, gift cards, shipping zones and
/// storefront content — §9.20, §9.23, §9.24, §9.30.
///
/// Admin-tier only throughout, following §9.3's split: every one of these changes what customers
/// are charged or what the storefront claims, which is at least as revenue-sensitive as the
/// coupon endpoints that set the precedent.
/// </summary>
[Tags("BackOffice - Merchandising")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
[Authorize(Policy = "BusinessMember")]
public class MerchandisingController(
    ICurrentUserContext currentUser,
    IPromotionService promotionService,
    ICustomerGroupService customerGroupService,
    IGiftCardService giftCardService,
    IStoreCreditService storeCreditService,
    IShippingService shippingService,
    IContentService contentService,
    IFileStorageService fileStorage) : VastoraControllerBase(currentUser)
{
    // --- §9.23: promotions ---

    [HttpGet("promotions")]
    public async Task<ActionResult<PagedResult<PromotionResponse>>> GetPromotions(
        string businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await promotionService.GetAllAsync(businessId, PageRequest.Of(page, pageSize), ct));

    [HttpPost("promotions")]
    public async Task<ActionResult<PromotionResponse>> CreatePromotion(string businessId, CreatePromotionRequest request, CancellationToken ct) =>
        Ok(await promotionService.CreateAsync(ResolvedTenantId, businessId, request, ct));

    [HttpPut("promotions/{promotionId}")]
    public async Task<ActionResult<PromotionResponse>> UpdatePromotion(string businessId, string promotionId, CreatePromotionRequest request, CancellationToken ct) =>
        Ok(await promotionService.UpdateAsync(ResolvedTenantId, businessId, promotionId, request, ct));

    [HttpDelete("promotions/{promotionId}")]
    public async Task<IActionResult> DeletePromotion(string businessId, string promotionId, CancellationToken ct)
    {
        await promotionService.DeleteAsync(ResolvedTenantId, businessId, promotionId, ct);
        return NoContent();
    }

    // --- §9.23: customer groups ---

    [HttpGet("customer-groups")]
    public async Task<ActionResult<PagedResult<CustomerGroupResponse>>> GetGroups(
        string businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await customerGroupService.GetAllAsync(businessId, PageRequest.Of(page, pageSize), ct));

    [HttpPost("customer-groups")]
    public async Task<ActionResult<CustomerGroupResponse>> CreateGroup(string businessId, CustomerGroupRequest request, CancellationToken ct) =>
        Ok(await customerGroupService.CreateAsync(ResolvedTenantId, businessId, request, ct));

    [HttpPut("customer-groups/{groupId}")]
    public async Task<ActionResult<CustomerGroupResponse>> UpdateGroup(string businessId, string groupId, CustomerGroupRequest request, CancellationToken ct) =>
        Ok(await customerGroupService.UpdateAsync(ResolvedTenantId, businessId, groupId, request, ct));

    [HttpPost("customer-groups/{groupId}/members")]
    public async Task<ActionResult<CustomerGroupResponse>> AddMembers(string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct) =>
        Ok(await customerGroupService.AddMembersAsync(ResolvedTenantId, businessId, groupId, request, ct));

    [HttpDelete("customer-groups/{groupId}/members")]
    public async Task<ActionResult<CustomerGroupResponse>> RemoveMembers(string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct) =>
        Ok(await customerGroupService.RemoveMembersAsync(ResolvedTenantId, businessId, groupId, request, ct));

    [HttpDelete("customer-groups/{groupId}")]
    public async Task<IActionResult> DeleteGroup(string businessId, string groupId, CancellationToken ct)
    {
        await customerGroupService.DeleteAsync(ResolvedTenantId, businessId, groupId, ct);
        return NoContent();
    }

    // --- §9.24: gift cards and store credit ---

    /// <summary>The plaintext code is in this response and nowhere else, ever. Capture it here or reissue.</summary>
    [HttpPost("gift-cards")]
    public async Task<ActionResult<GiftCardResponse>> IssueGiftCard(string businessId, IssueGiftCardRequest request, CancellationToken ct) =>
        Ok(await giftCardService.IssueAsync(ResolvedTenantId, businessId, request, ct));

    [HttpGet("gift-cards")]
    public async Task<ActionResult<PagedResult<GiftCardResponse>>> GetGiftCards(
        string businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await giftCardService.GetAllAsync(businessId, PageRequest.Of(page, pageSize), ct));

    [HttpDelete("gift-cards/{giftCardId}")]
    public async Task<IActionResult> DeactivateGiftCard(string businessId, string giftCardId, CancellationToken ct)
    {
        await giftCardService.DeactivateAsync(ResolvedTenantId, businessId, giftCardId, ct);
        return NoContent();
    }

    [HttpGet("customers/{customerUserId}/store-credit")]
    public async Task<ActionResult<StoreCreditBalanceResponse>> GetStoreCredit(string businessId, string customerUserId, CancellationToken ct) =>
        Ok(await storeCreditService.GetStatementAsync(businessId, customerUserId, ct));

    [HttpPost("customers/{customerUserId}/store-credit")]
    public async Task<IActionResult> GrantStoreCredit(string businessId, string customerUserId, GrantStoreCreditRequest request, CancellationToken ct)
    {
        await storeCreditService.RecordAsync(
            ResolvedTenantId, businessId, customerUserId, request.Amount,
            StoreCreditReason.ManualAdjustment, request.Note, ct: ct);
        return NoContent();
    }

    // --- §9.20: shipping zones ---

    [HttpGet("shipping-zones")]
    public async Task<ActionResult<PagedResult<ShippingZoneResponse>>> GetZones(
        string businessId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await shippingService.GetZonesAsync(businessId, PageRequest.Of(page, pageSize), ct));

    [HttpPost("shipping-zones")]
    public async Task<ActionResult<ShippingZoneResponse>> CreateZone(string businessId, CreateShippingZoneRequest request, CancellationToken ct) =>
        Ok(await shippingService.CreateZoneAsync(ResolvedTenantId, businessId, request, ct));

    [HttpPut("shipping-zones/{zoneId}")]
    public async Task<ActionResult<ShippingZoneResponse>> UpdateZone(string businessId, string zoneId, CreateShippingZoneRequest request, CancellationToken ct) =>
        Ok(await shippingService.UpdateZoneAsync(ResolvedTenantId, businessId, zoneId, request, ct));

    [HttpDelete("shipping-zones/{zoneId}")]
    public async Task<IActionResult> DeleteZone(string businessId, string zoneId, CancellationToken ct)
    {
        await shippingService.DeleteZoneAsync(ResolvedTenantId, businessId, zoneId, ct);
        return NoContent();
    }

    // --- §9.30: storefront content ---

    [HttpGet("content")]
    public async Task<ActionResult<PagedResult<ContentBlockResponse>>> GetContent(
        string businessId, [FromQuery] ContentBlockType? type,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await contentService.GetAllAsync(businessId, type, PageRequest.Of(page, pageSize), ct));

    [HttpPost("content")]
    public async Task<ActionResult<ContentBlockResponse>> CreateContent(string businessId, ContentBlockRequest request, CancellationToken ct) =>
        Ok(await contentService.CreateAsync(ResolvedTenantId, businessId, request, ct));

    [HttpPut("content/{blockId}")]
    public async Task<ActionResult<ContentBlockResponse>> UpdateContent(string businessId, string blockId, ContentBlockRequest request, CancellationToken ct) =>
        Ok(await contentService.UpdateAsync(ResolvedTenantId, businessId, blockId, request, ct));

    [HttpDelete("content/{blockId}")]
    public async Task<IActionResult> DeleteContent(string businessId, string blockId, CancellationToken ct)
    {
        await contentService.DeleteAsync(ResolvedTenantId, businessId, blockId, ct);
        return NoContent();
    }

    /// <summary>Uploads one image, replaces ContentBlock.ImageUrl. Local disk storage — see IFileStorageService.</summary>
    [HttpPost("content/{blockId}/image")]
    [RequestSizeLimit(ImageUploadPolicy.MaxFileSizeBytes)]
    public async Task<ActionResult<ContentBlockResponse>> UploadContentImage(string businessId, string blockId, IFormFile file, CancellationToken ct)
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
        var url = await fileStorage.SaveAsync(businessId, stream, extension, ct);
        var result = await contentService.SetImageAsync(ResolvedTenantId, businessId, blockId, url, ct);
        return Ok(result);
    }
}
