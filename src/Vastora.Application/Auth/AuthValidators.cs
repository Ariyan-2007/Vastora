using FluentValidation;

namespace Vastora.Application.Auth;

public class BackOfficeLoginRequestValidator : AbstractValidator<BackOfficeLoginRequest>
{
    public BackOfficeLoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class StorefrontLoginRequestValidator : AbstractValidator<StorefrontLoginRequest>
{
    public StorefrontLoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class StorefrontRegisterRequestValidator : AbstractValidator<StorefrontRegisterRequest>
{
    public StorefrontRegisterRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Phone).NotEmpty();
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
