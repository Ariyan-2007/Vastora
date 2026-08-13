using FluentValidation;

namespace Vastora.Application.Cart;

public class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
    }
}

public class ApplyCartCouponRequestValidator : AbstractValidator<ApplyCartCouponRequest>
{
    public ApplyCartCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty();
    }
}
