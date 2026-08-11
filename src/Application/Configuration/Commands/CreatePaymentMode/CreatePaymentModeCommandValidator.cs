using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreatePaymentMode;

public sealed class CreatePaymentModeCommandValidator : AbstractValidator<CreatePaymentModeCommand>
{
    public CreatePaymentModeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
