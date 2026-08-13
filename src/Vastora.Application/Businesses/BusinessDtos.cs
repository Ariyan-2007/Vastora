using Vastora.Domain.Enums;

namespace Vastora.Application.Businesses;

public record BusinessResponse(
    string Id,
    string TenantId,
    string Name,
    string Slug,
    string? CustomDomain,
    string Description,
    string LogoUrl,
    string BannerUrl,
    string ThemeColor,
    string Currency,
    string ContactEmail,
    string ContactPhone,
    BusinessStatus Status,
    DateTime CreatedAt);

public record CreateBusinessRequest(
    string Name,
    string? Slug,
    string Description,
    string ContactEmail,
    string ContactPhone,
    string Currency = "USD");

public record UpdateBusinessRequest(
    string Name,
    string Description,
    string LogoUrl,
    string BannerUrl,
    string ThemeColor,
    string ContactEmail,
    string ContactPhone,
    string Currency);
