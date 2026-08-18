using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Webhooks;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Reviews;

public record CreateReviewRequest(string ProductId, int Rating, string Title, string Body);

public record ModerateReviewRequest(ReviewStatus Status);

public record MerchantReplyRequest(string Reply);

public record ReviewResponse(
    string Id,
    string ProductId,
    string CustomerName,
    int Rating,
    string Title,
    string Body,
    ReviewStatus Status,
    bool IsVerifiedPurchase,
    string? MerchantReply,
    DateTime? MerchantRepliedAt,
    int HelpfulCount,
    DateTime CreatedAt);

/// <summary>Rating distribution for a product, for the storefront's histogram.</summary>
public record ReviewSummaryResponse(string ProductId, double AverageRating, int ReviewCount, Dictionary<int, int> RatingCounts);

/// <summary>§9.25.</summary>
public interface IReviewService
{
    /// <summary>Published reviews only — this is the public storefront read.</summary>
    Task<PagedResult<ReviewResponse>> GetPublishedForProductAsync(string businessId, string productId, PageRequest page, CancellationToken ct = default);

    Task<ReviewSummaryResponse> GetSummaryAsync(string businessId, string productId, CancellationToken ct = default);

    /// <summary>Every review regardless of status — the BackOffice moderation queue.</summary>
    Task<PagedResult<ReviewResponse>> GetAllAsync(string businessId, ReviewStatus? status, PageRequest page, CancellationToken ct = default);

    Task<ReviewResponse> SubmitAsync(string tenantId, string businessId, string customerUserId, CreateReviewRequest request, CancellationToken ct = default);

    Task<ReviewResponse> ModerateAsync(string tenantId, string businessId, string reviewId, ModerateReviewRequest request, CancellationToken ct = default);

    Task<ReviewResponse> ReplyAsync(string tenantId, string businessId, string reviewId, MerchantReplyRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string reviewId, CancellationToken ct = default);

    Task<ReviewResponse> MarkHelpfulAsync(string businessId, string reviewId, CancellationToken ct = default);
}

/// <inheritdoc cref="IReviewService"/>
public class ReviewService(
    IMongoRepository<Review> reviews,
    IMongoRepository<Order> orders,
    IMongoRepository<Product> products,
    IMongoRepository<Business> businesses,
    IMongoRepository<AppUser> users,
    IProductStockStore stockStore,
    IWebhookPublisher webhookPublisher) : IReviewService
{
    public async Task<PagedResult<ReviewResponse>> GetPublishedForProductAsync(string businessId, string productId, PageRequest page, CancellationToken ct = default)
    {
        var result = await reviews.FindPagedAsync(
            r => r.BusinessId == businessId && r.ProductId == productId && r.Status == ReviewStatus.Published,
            page, r => r.CreatedAt, ct: ct);

        return result.Map(Map);
    }

    public async Task<ReviewSummaryResponse> GetSummaryAsync(string businessId, string productId, CancellationToken ct = default)
    {
        var published = await reviews.FindAsync(
            r => r.BusinessId == businessId && r.ProductId == productId && r.Status == ReviewStatus.Published, ct);

        var counts = Enumerable.Range(1, 5).ToDictionary(star => star, star => published.Count(r => r.Rating == star));

        return new ReviewSummaryResponse(
            productId,
            published.Count == 0 ? 0d : Math.Round(published.Average(r => r.Rating), 2),
            published.Count,
            counts);
    }

    public async Task<PagedResult<ReviewResponse>> GetAllAsync(string businessId, ReviewStatus? status, PageRequest page, CancellationToken ct = default)
    {
        var result = await reviews.FindPagedAsync(
            r => r.BusinessId == businessId && (status == null || r.Status == status),
            page, r => r.CreatedAt, ct: ct);

        return result.Map(Map);
    }

    public async Task<ReviewResponse> SubmitAsync(string tenantId, string businessId, string customerUserId, CreateReviewRequest request, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (!business.ReviewsEnabled)
        {
            throw new ConflictException("This shop has reviews turned off.");
        }

        var product = await products.GetByIdAsync(request.ProductId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), request.ProductId);
        }

        // One review per customer per product. Editing your own review is fine; farming a product's
        // rating by submitting the same opinion repeatedly is not.
        var existing = await reviews.FindOneAsync(
            r => r.BusinessId == businessId && r.ProductId == request.ProductId && r.CustomerUserId == customerUserId, ct);
        if (existing is not null)
        {
            throw new ConflictException("You've already reviewed this product.");
        }

        // Verified-purchase is established from real order history, never accepted from the
        // client — it is the badge that makes a review worth trusting. Delivered or PickedUp
        // both count (§9.47) — a customer who picked an order up in-store bought it just as
        // genuinely as one who had it delivered.
        var purchase = (await orders.FindAsync(
                o => o.BusinessId == businessId
                     && o.CustomerUserId == customerUserId
                     && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp), ct))
            .FirstOrDefault(o => o.Items.Any(i => i.ProductId == request.ProductId));

        var customer = await users.GetByIdAsync(customerUserId, ct);

        var review = new Review
        {
            TenantId = tenantId,
            BusinessId = businessId,
            ProductId = request.ProductId,
            CustomerUserId = customerUserId,
            CustomerName = customer?.FullName ?? "Customer",
            Rating = Math.Clamp(request.Rating, 1, 5),
            Title = request.Title,
            Body = request.Body,
            IsVerifiedPurchase = purchase is not null,
            VerifiedOrderId = purchase?.Id,
            Status = business.AutoPublishReviews ? ReviewStatus.Published : ReviewStatus.Pending
        };

        await reviews.AddAsync(review, ct);

        if (review.Status == ReviewStatus.Published)
        {
            await RefreshAggregateAsync(businessId, request.ProductId, ct);
        }

        await webhookPublisher.PublishAsync(tenantId, businessId, WebhookEvents.ReviewSubmitted,
            new { reviewId = review.Id, productId = review.ProductId, rating = review.Rating }, ct);

        return Map(review);
    }

    public async Task<ReviewResponse> ModerateAsync(string tenantId, string businessId, string reviewId, ModerateReviewRequest request, CancellationToken ct = default)
    {
        var review = await GetScopedAsync(tenantId, businessId, reviewId, ct);

        review.Status = request.Status;
        await reviews.UpdateAsync(review, ct);

        // Recomputed on every moderation decision in either direction — unpublishing a review has
        // to move the average back down, not just publishing move it up.
        await RefreshAggregateAsync(businessId, review.ProductId, ct);

        return Map(review);
    }

    public async Task<ReviewResponse> ReplyAsync(string tenantId, string businessId, string reviewId, MerchantReplyRequest request, CancellationToken ct = default)
    {
        var review = await GetScopedAsync(tenantId, businessId, reviewId, ct);

        review.MerchantReply = request.Reply;
        review.MerchantRepliedAt = DateTime.UtcNow;
        await reviews.UpdateAsync(review, ct);

        return Map(review);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string reviewId, CancellationToken ct = default)
    {
        var review = await GetScopedAsync(tenantId, businessId, reviewId, ct);
        await reviews.DeleteAsync(reviewId, ct: ct);
        await RefreshAggregateAsync(businessId, review.ProductId, ct);
    }

    public async Task<ReviewResponse> MarkHelpfulAsync(string businessId, string reviewId, CancellationToken ct = default)
    {
        var review = await reviews.GetByIdAsync(reviewId, ct);
        if (review is null || review.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Review), reviewId);
        }

        // Atomic — this is the one review counter several anonymous visitors can hit at once.
        var updated = await reviews.IncrementAsync(reviewId, r => r.HelpfulCount, 1, ct);
        return Map(updated ?? review);
    }

    /// <summary>
    /// Recomputes Product.AverageRating/ReviewCount from published reviews only. Denormalised onto
    /// the product so the catalog can sort by rating without a join, and written through the
    /// atomic store so two moderation actions landing together can't lose one another.
    /// </summary>
    private async Task RefreshAggregateAsync(string businessId, string productId, CancellationToken ct)
    {
        var published = await reviews.FindAsync(
            r => r.BusinessId == businessId && r.ProductId == productId && r.Status == ReviewStatus.Published, ct);

        var average = published.Count == 0 ? 0d : Math.Round(published.Average(r => r.Rating), 2);
        await stockStore.SetReviewAggregateAsync(productId, average, published.Count, ct);
    }

    private async Task<Review> GetScopedAsync(string tenantId, string businessId, string reviewId, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(reviewId, ct);
        if (review is null || review.TenantId != tenantId || review.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Review), reviewId);
        }

        return review;
    }

    private static ReviewResponse Map(Review r) => new(
        r.Id, r.ProductId, r.CustomerName, r.Rating, r.Title, r.Body, r.Status,
        r.IsVerifiedPurchase, r.MerchantReply, r.MerchantRepliedAt, r.HelpfulCount, r.CreatedAt);
}
