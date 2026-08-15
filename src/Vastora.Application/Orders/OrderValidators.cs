using FluentValidation;

namespace Vastora.Application.Orders;

public class CheckoutRequestValidator : AbstractValidator<CheckoutRequest>
{
    public CheckoutRequestValidator()
    {
        RuleFor(x => x.ShippingAddress).NotNull();
        RuleFor(x => x.ShippingAddress.Line1).NotEmpty().When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().When(x => x.ShippingAddress is not null);
        RuleFor(x => x.ShippingAddress.Phone).NotEmpty().When(x => x.ShippingAddress is not null);
        RuleFor(x => x.DeliveryFee).GreaterThanOrEqualTo(0);
    }
}

public class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}

public class AssignDeliveryAgentRequestValidator : AbstractValidator<AssignDeliveryAgentRequest>
{
    public AssignDeliveryAgentRequestValidator()
    {
        RuleFor(x => x.DeliveryAgentUserId).NotEmpty();
    }
}

public class UpdatePaymentStatusRequestValidator : AbstractValidator<UpdatePaymentStatusRequest>
{
    public UpdatePaymentStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
