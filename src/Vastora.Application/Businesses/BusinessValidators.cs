using FluentValidation;

namespace Vastora.Application.Businesses;

public class CreateBusinessRequestValidator : AbstractValidator<CreateBusinessRequest>
{
    public CreateBusinessRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}

public class WipeBusinessRequestValidator : AbstractValidator<WipeBusinessRequest>
{
    public WipeBusinessRequestValidator()
    {
        RuleFor(x => x.ConfirmSlug).NotEmpty();
    }
}
