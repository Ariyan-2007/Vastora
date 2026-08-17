using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice self-management of one Business's profile, reachable by anyone scoped to it.</summary>
[Tags("BackOffice - Business")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class BusinessesController(ICurrentUserContext currentUser, IBusinessService businessService, IFileStorageService fileStorage)
    : VastoraControllerBase(currentUser)
{
    private const string ProfileEditorRoles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}";

    [HttpGet]
    public async Task<ActionResult<BusinessResponse>> GetById(string businessId, CancellationToken ct)
    {
        var result = await businessService.GetByIdForPlatformAsync(businessId, ct);
        return Ok(result);
    }

    [HttpPut]
    [Authorize(Roles = ProfileEditorRoles)]
    public async Task<ActionResult<BusinessResponse>> Update(string businessId, UpdateBusinessRequest request, CancellationToken ct)
    {
        var result = await businessService.UpdateAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    /// <summary>Turns the DeliveryAgent workflow on/off for this Business — see Roadmap §9.14.</summary>
    [HttpPatch("delivery-module")]
    [Authorize(Roles = ProfileEditorRoles)]
    public async Task<ActionResult<BusinessResponse>> UpdateDeliveryModule(string businessId, UpdateDeliveryModuleRequest request, CancellationToken ct)
    {
        var result = await businessService.UpdateDeliveryModuleAsync(ResolvedTenantId, businessId, request.Enabled, ct);
        return Ok(result);
    }

    /// <summary>Uploads one image, replaces Business.LogoUrl. Local disk storage — see IFileStorageService.</summary>
    [HttpPost("logo")]
    [Authorize(Roles = ProfileEditorRoles)]
    [RequestSizeLimit(ImageUploadPolicy.MaxFileSizeBytes)]
    public async Task<ActionResult<BusinessResponse>> UploadLogo(string businessId, IFormFile file, CancellationToken ct)
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
        var result = await businessService.SetLogoAsync(ResolvedTenantId, businessId, url, ct);
        return Ok(result);
    }

    /// <summary>Uploads one image, replaces Business.BannerUrl. Local disk storage — see IFileStorageService.</summary>
    [HttpPost("banner")]
    [Authorize(Roles = ProfileEditorRoles)]
    [RequestSizeLimit(ImageUploadPolicy.MaxFileSizeBytes)]
    public async Task<ActionResult<BusinessResponse>> UploadBanner(string businessId, IFormFile file, CancellationToken ct)
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
        var result = await businessService.SetBannerAsync(ResolvedTenantId, businessId, url, ct);
        return Ok(result);
    }
}
