using FluentValidation;

namespace Vastora.Application.Tenants;

public class TenantSignUpRequestValidator : AbstractValidator<TenantSignUpRequest>
{
    public TenantSignUpRequestValidator()
    {
        RuleFor(x => x.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TenantType).IsInEnum();
        RuleFor(x => x.OwnerFullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.OwnerPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.OwnerPhone).NotEmpty();
        RuleFor(x => x.InitialBusinessName).NotEmpty().MaximumLength(200);
    }
}
