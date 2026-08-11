using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdatePaymentMode;

public sealed class UpdatePaymentModeCommandValidator : AbstractValidator<UpdatePaymentModeCommand>
{
    public UpdatePaymentModeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
