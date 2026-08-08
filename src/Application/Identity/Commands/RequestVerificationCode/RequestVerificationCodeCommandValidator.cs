using FluentValidation;

namespace ErpApp.Application.Identity.Commands.RequestVerificationCode;

public sealed class RequestVerificationCodeCommandValidator : AbstractValidator<RequestVerificationCodeCommand>
{
    public RequestVerificationCodeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
