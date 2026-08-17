using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.30. One shopper-visible piece of storefront content — a homepage banner, a static page
/// (About / Terms / Privacy), a nav menu entry, or an article. A single polymorphic collection
/// rather than four near-identical ones: they share slug, schedule, ordering and publish state,
/// and differ only in which fields the frontend reads.
/// </summary>
public class ContentBlock : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public ContentBlockType Type { get; set; } = ContentBlockType.Page;

    /// <summary>Unique per (Business, Type). For a Page this is its public URL segment.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Markdown for Page/Article, ignored for Banner/MenuItem.</summary>
    public string Body { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>Where clicking this goes — a banner's CTA, a menu item's destination.</summary>
    public string LinkUrl { get; set; } = string.Empty;

    public string LinkLabel { get; set; } = string.Empty;

    /// <summary>MenuItem only — nests a menu without a second collection.</summary>
    public string? ParentId { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublished { get; set; }

    /// <summary>Scheduled visibility window — a banner for a sale that starts Friday.</summary>
    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    public string MetaTitle { get; set; } = string.Empty;

    public string MetaDescription { get; set; } = string.Empty;

    public bool IsVisibleNow(DateTime now) =>
        IsPublished
        && (StartsAt is null || StartsAt <= now)
        && (EndsAt is null || EndsAt > now);
}
