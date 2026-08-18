using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.GiftCards;
using Vastora.Application.Pricing;
using Vastora.Application.Promotions;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Cart;

public class CartService(
    IMongoRepository<Domain.Entities.Cart> carts,
    IMongoRepository<Product> products,
    IMongoRepository<Business> businesses,
    ICouponService couponService,
    IPromotionService promotionService,
    IGiftCardService giftCardService,
    IPricingService pricingService) : ICartService
{
    public async Task<CartResponse> GetAsync(string businessId, CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> AddItemAsync(string tenantId, string businessId, CartOwner owner, AddCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            throw new ConflictException("Quantity must be greater than zero.");
        }

        var product = await products.GetByIdAsync(request.ProductId, ct);
        if (product is null || product.BusinessId != businessId || !product.IsPubliclyVisibleNow(DateTime.UtcNow))
        {
            throw new NotFoundException(nameof(Product), request.ProductId);
        }

        // §9.22: a product with variants can only be bought *as* a variant. Letting a bare
        // ProductId through would mean selling from a stock pool that doesn't exist.
        ProductVariant? variant = null;
        if (product.Variants.Count > 0)
        {
            if (string.IsNullOrEmpty(request.VariantId))
            {
                throw new ConflictException($"'{product.Name}' has options — choose one before adding it to your cart.");
            }

            variant = product.Variants.FirstOrDefault(v => v.Id == request.VariantId)
                ?? throw new NotFoundException("ProductVariant", request.VariantId);
        }

        var cart = await GetOrCreateAsync(tenantId, businessId, owner, ct);

        var existing = cart.Items.FirstOrDefault(i => i.Matches(request.ProductId, variant?.Id));
        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                ProductId = product.Id,
                VariantId = variant?.Id,
                VariantSummary = variant?.AttributeSummary,
                ProductName = product.Name,
                UnitPrice = variant?.PriceOverride ?? product.EffectivePrice,
                Quantity = request.Quantity
            });
        }

        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> UpdateItemAsync(string businessId, CartOwner owner, string productId, UpdateCartItemRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        var item = cart.Items.FirstOrDefault(i => i.Matches(productId, request.VariantId))
            ?? throw new NotFoundException(nameof(CartItem), productId);

        if (request.Quantity <= 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            item.Quantity = request.Quantity;
        }

        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> RemoveItemAsync(string businessId, CartOwner owner, string productId, string? variantId, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);

        // A null variantId from a client that predates variants removes every line for the
        // product, which is the least surprising reading of "remove this product".
        cart.Items.RemoveAll(i => variantId is null ? i.ProductId == productId : i.Matches(productId, variantId));

        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> ApplyCouponAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);

        // Validated now so an invalid code fails here, where the customer can see why, rather
        // than silently contributing nothing at checkout.
        await couponService.ValidateAndPriceAsync(businessId, request.Code, subtotal, ct);

        cart.CouponCode = request.Code.Trim().ToUpperInvariant();
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> RemoveCouponAsync(string businessId, CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.CouponCode = null;
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> ApplyPromotionCodeAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        var code = request.Code.Trim().ToUpperInvariant();

        if (cart.PromotionCodes.Contains(code))
        {
            return await MapAsync(cart, owner, ct);
        }

        cart.PromotionCodes.Add(code);

        // Evaluated against the real cart before being accepted: a code that qualifies for nothing
        // is rejected rather than sitting on the cart looking applied.
        //
        // §9.46: this used to build its own PromotionContext with CustomerGroupIds hardcoded to
        // [] and IsFirstOrder hardcoded to false — so a promotion actually scoped to a customer
        // group, or restricted to a first order, was rejected right here even for an eligible
        // customer, despite PriceAsync (used by the cart preview and checkout) evaluating the same
        // code correctly moments later. BuildPromotionContextAsync is the same lookup PriceAsync
        // itself uses, so the two can no longer disagree about who qualifies.
        var lines = await pricingService.ResolveLinesAsync(businessId, cart.Items, owner.CustomerUserId, ct);
        var promotionContext = await pricingService.BuildPromotionContextAsync(
            businessId, owner.CustomerUserId, lines, cart.PromotionCodes, ct);
        var evaluation = await promotionService.EvaluateAsync(promotionContext, ct);

        if (evaluation.AppliedPromotionIds.Count == 0)
        {
            throw new ConflictException($"Code '{code}' isn't valid for this cart.");
        }

        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> RemovePromotionCodeAsync(string businessId, CartOwner owner, string code, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.PromotionCodes.RemoveAll(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> ApplyGiftCardAsync(string businessId, CartOwner owner, ApplyCartCouponRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        var code = request.Code.Trim().ToUpperInvariant();

        // Checked now, against the ledger, so a dead or mistyped code fails where the shopper can
        // see it — the same reasoning ApplyCouponAsync already applies.
        var balance = await giftCardService.CheckBalanceAsync(businessId, code, ct);
        if (!balance.IsRedeemable)
        {
            throw new ConflictException($"Gift card '{code}' has no balance or has expired.");
        }

        if (!cart.GiftCardCodes.Contains(code))
        {
            cart.GiftCardCodes.Add(code);
            await carts.UpdateAsync(cart, ct);
        }

        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> RemoveGiftCardAsync(string businessId, CartOwner owner, string code, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.GiftCardCodes.RemoveAll(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> SetUseStoreCreditAsync(string businessId, CartOwner owner, bool useStoreCredit, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.UseStoreCredit = useStoreCredit;
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<CartResponse> SetFulfillmentMethodAsync(string businessId, CartOwner owner, FulfillmentMethod fulfillmentMethod, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.FulfillmentMethod = fulfillmentMethod;
        await carts.UpdateAsync(cart, ct);
        return await MapAsync(cart, owner, ct);
    }

    public async Task<List<AvailableOfferResponse>> GetAvailableOffersAsync(string businessId, CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
        return await pricingService.GetAvailableOffersAsync(businessId, subtotal, ct);
    }

    public async Task ClearAsync(string businessId, CartOwner owner, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, owner, ct);
        cart.Items.Clear();
        cart.CouponCode = null;
        cart.PromotionCodes.Clear();
        cart.GiftCardCodes.Clear();
        cart.UseStoreCredit = false;
        cart.FulfillmentMethod = FulfillmentMethod.Delivery;
        await carts.UpdateAsync(cart, ct);
    }

    public async Task<CartResponse> MergeGuestCartAsync(string tenantId, string businessId, string customerUserId, string guestToken, CancellationToken ct = default)
    {
        var owner = CartOwner.ForCustomer(customerUserId);

        var guestCart = await carts.FindOneAsync(c => c.BusinessId == businessId && c.GuestToken == guestToken, ct);
        if (guestCart is null || guestCart.Items.Count == 0)
        {
            return await GetAsync(businessId, owner, ct);
        }

        var customerCart = await GetOrCreateAsync(tenantId, businessId, owner, ct);

        foreach (var guestItem in guestCart.Items)
        {
            var existing = customerCart.Items.FirstOrDefault(i => i.Matches(guestItem.ProductId, guestItem.VariantId));
            if (existing is not null)
            {
                // Summed, not replaced. The shopper put the item in a cart twice and meant it both
                // times; silently discarding one of them is the surprising behaviour.
                existing.Quantity += guestItem.Quantity;
            }
            else
            {
                customerCart.Items.Add(guestItem);
            }
        }

        // A code the guest entered survives the merge, unless the account already had one — the
        // customer's own choice wins over one carried in from an anonymous session.
        customerCart.CouponCode ??= guestCart.CouponCode;
        foreach (var code in guestCart.PromotionCodes.Where(c => !customerCart.PromotionCodes.Contains(c)))
        {
            customerCart.PromotionCodes.Add(code);
        }

        foreach (var code in guestCart.GiftCardCodes.Where(c => !customerCart.GiftCardCodes.Contains(c)))
        {
            customerCart.GiftCardCodes.Add(code);
        }

        await carts.UpdateAsync(customerCart, ct);
        await carts.HardDeleteAsync(guestCart.Id, ct);

        return await MapAsync(customerCart, owner, ct);
    }

    private async Task<Domain.Entities.Cart> GetOrCreateAsync(string tenantId, string businessId, CartOwner owner, CancellationToken ct)
    {
        var existing = await FindStoredAsync(businessId, owner, ct);
        if (existing is not null)
        {
            return existing;
        }

        var cart = new Domain.Entities.Cart
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CustomerUserId = owner.CustomerUserId ?? string.Empty,
            // A guest arriving without a token gets one minted here; it comes back on the response
            // and is the client's handle on this cart from then on.
            GuestToken = owner.IsGuest ? owner.GuestToken ?? Guid.NewGuid().ToString("N") : null
        };

        return await carts.AddAsync(cart, ct);
    }

    private async Task<Domain.Entities.Cart?> FindStoredAsync(string businessId, CartOwner owner, CancellationToken ct) =>
        owner.IsGuest
            ? string.IsNullOrEmpty(owner.GuestToken)
                ? null
                : await carts.FindOneAsync(c => c.BusinessId == businessId && c.GuestToken == owner.GuestToken, ct)
            : await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == owner.CustomerUserId, ct);

    /// <summary>Reads never create. A GET on an empty cart returns an empty cart, not a stored one.</summary>
    private async Task<Domain.Entities.Cart> FindAsync(string businessId, CartOwner owner, CancellationToken ct) =>
        await FindStoredAsync(businessId, owner, ct)
        ?? new Domain.Entities.Cart
        {
            BusinessId = businessId,
            CustomerUserId = owner.CustomerUserId ?? string.Empty,
            GuestToken = owner.GuestToken
        };

    /// <summary>
    /// Priced through the same IPricingService checkout uses. Tax stays out of EstimatedTotal —
    /// it depends on a delivery address this cart doesn't have yet — but delivery fee (§9.44) is
    /// resolvable without one (a flat DefaultDeliveryFee, or a shipping zone that doesn't need a
    /// specific address to match) and is surfaced as its own field rather than folded silently
    /// into a total that would then change again at checkout.
    /// </summary>
    private async Task<CartResponse> MapAsync(Domain.Entities.Cart cart, CartOwner owner, CancellationToken ct)
    {
        var items = cart.Items
            .Select(i => new CartItemResponse(
                i.ProductId, i.VariantId, i.VariantSummary, i.ProductName, i.UnitPrice, i.Quantity, i.UnitPrice * i.Quantity))
            .ToList();

        var subtotal = items.Sum(i => i.LineTotal);

        var business = await businesses.GetByIdAsync(cart.BusinessId, ct);
        var currency = business?.Currency ?? string.Empty;

        if (cart.Items.Count == 0 || business is null)
        {
            return new CartResponse(cart.Id, cart.BusinessId, items, cart.CouponCode, cart.PromotionCodes,
                subtotal, [], 0m, subtotal, currency, 0, cart.GuestToken,
                cart.GiftCardCodes, 0m, cart.UseStoreCredit, 0m, subtotal,
                cart.FulfillmentMethod, 0m, null, []);
        }

        var lines = await pricingService.ResolveLinesAsync(cart.BusinessId, cart.Items, owner.CustomerUserId, ct);

        // §9.43/§9.44: cart.GiftCardCodes, cart.UseStoreCredit and cart.FulfillmentMethod used to
        // be dropped here — set on the cart but never fed back into pricing, so a shopper who
        // applied a gift card, opted into store credit, or chose Pickup saw none of that reflected
        // until checkout actually charged them. The `0m` literal below was its own separate bug:
        // ExplicitDeliveryFee is nullable specifically so "no override" can be expressed, and
        // passing 0m (not null) forced the delivery fee to always resolve as exactly $0 here,
        // silently, regardless of the business's real shipping zones or DefaultDeliveryFee.
        var breakdown = await pricingService.PriceAsync(
            new PricingContext(business, owner.CustomerUserId, null, cart.CouponCode,
                cart.PromotionCodes, cart.GiftCardCodes, null, null, cart.UseStoreCredit, cart.FulfillmentMethod),
            lines, ct);

        return new CartResponse(
            cart.Id, cart.BusinessId, items, cart.CouponCode, cart.PromotionCodes,
            breakdown.Subtotal, [.. breakdown.Discounts], breakdown.DiscountTotal,
            breakdown.Subtotal - breakdown.DiscountTotal, currency,
            items.Sum(i => i.Quantity), cart.GuestToken,
            cart.GiftCardCodes, breakdown.GiftCardTotal, cart.UseStoreCredit,
            breakdown.StoreCreditApplied, breakdown.AmountDue,
            cart.FulfillmentMethod, breakdown.DeliveryFee, breakdown.ShippingMethodName, [.. breakdown.ShippingOptions]);
    }
}
