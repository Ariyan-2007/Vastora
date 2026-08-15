using FluentValidation;

namespace Vastora.Application.Inventory;

public class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
    }
}
