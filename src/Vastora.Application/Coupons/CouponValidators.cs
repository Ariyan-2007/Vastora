using FluentValidation;

namespace Vastora.Application.Coupons;

public class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.DiscountValue).GreaterThan(0);
        // §9.43: ExpiresAt is now optional (null = never expires) — only checked against StartsAt when present.
        RuleFor(x => x.ExpiresAt).GreaterThan(x => x.StartsAt).When(x => x.ExpiresAt is not null);
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

public class UpdateCouponRequestValidator : AbstractValidator<UpdateCouponRequest>
{
    public UpdateCouponRequestValidator()
    {
        RuleFor(x => x.Visibility).IsInEnum();
    }
}
