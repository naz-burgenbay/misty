using FluentValidation;

namespace Misty.Application.Users;

public sealed class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(32)
            .Matches(@"^[a-zA-Z0-9_]+$")
            .WithMessage("Username may only contain letters, numbers, and underscores.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(256)
            .EmailAddress();

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(64);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .MaximumLength(64);
    }
}
