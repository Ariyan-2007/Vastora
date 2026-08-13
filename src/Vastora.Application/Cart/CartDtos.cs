namespace Vastora.Application.Cart;

public record CartItemResponse(string ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record CartResponse(
    string Id,
    string BusinessId,
    List<CartItemResponse> Items,
    string? CouponCode,
    decimal Subtotal);

public record AddCartItemRequest(string ProductId, int Quantity);

public record UpdateCartItemRequest(int Quantity);

public record ApplyCartCouponRequest(string Code);
