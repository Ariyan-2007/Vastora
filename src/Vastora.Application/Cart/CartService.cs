using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Cart;

public class CartService(
    IMongoRepository<Domain.Entities.Cart> carts,
    IMongoRepository<Product> products,
    ICouponService couponService) : ICartService
{
    public async Task<CartResponse> GetAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, customerUserId, ct);
        return Map(cart);
    }

    public async Task<CartResponse> AddItemAsync(string tenantId, string businessId, string customerUserId, AddCartItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
        {
            throw new ConflictException("Quantity must be greater than zero.");
        }

        var product = await products.GetByIdAsync(request.ProductId, ct);
        if (product is null || product.BusinessId != businessId || product.Status != ProductStatus.Active)
        {
            throw new NotFoundException(nameof(Product), request.ProductId);
        }

        var cart = await GetOrCreateAsync(tenantId, businessId, customerUserId, ct);

        var existing = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.EffectivePrice,
                Quantity = request.Quantity
            });
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);
        return Map(cart);
    }

    public async Task<CartResponse> UpdateItemAsync(string businessId, string customerUserId, string productId, UpdateCartItemRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, customerUserId, ct);
        var item = cart.Items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new NotFoundException(nameof(CartItem), productId);

        if (request.Quantity <= 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            item.Quantity = request.Quantity;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);
        return Map(cart);
    }

    public async Task<CartResponse> RemoveItemAsync(string businessId, string customerUserId, string productId, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, customerUserId, ct);
        cart.Items.RemoveAll(i => i.ProductId == productId);
        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);
        return Map(cart);
    }

    public async Task<CartResponse> ApplyCouponAsync(string businessId, string customerUserId, ApplyCartCouponRequest request, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, customerUserId, ct);
        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);

        await couponService.ValidateAndPriceAsync(businessId, request.Code, subtotal, ct);

        cart.CouponCode = request.Code.Trim().ToUpperInvariant();
        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);
        return Map(cart);
    }

    public async Task ClearAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var cart = await FindAsync(businessId, customerUserId, ct);
        cart.Items.Clear();
        cart.CouponCode = null;
        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);
    }

    private async Task<Domain.Entities.Cart> GetOrCreateAsync(string tenantId, string businessId, string customerUserId, CancellationToken ct)
    {
        var existing = await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var cart = new Domain.Entities.Cart
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CustomerUserId = customerUserId
        };
        return await carts.AddAsync(cart, ct);
    }

    private async Task<Domain.Entities.Cart> FindAsync(string businessId, string customerUserId, CancellationToken ct)
    {
        return await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct)
            ?? new Domain.Entities.Cart { BusinessId = businessId, CustomerUserId = customerUserId };
    }

    private static CartResponse Map(Domain.Entities.Cart cart)
    {
        var items = cart.Items.Select(i => new CartItemResponse(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.UnitPrice * i.Quantity)).ToList();
        return new CartResponse(cart.Id, cart.BusinessId, items, cart.CouponCode, items.Sum(i => i.LineTotal));
    }
}
