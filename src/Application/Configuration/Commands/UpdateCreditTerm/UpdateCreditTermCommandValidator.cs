using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateCreditTerm;

public sealed class UpdateCreditTermCommandValidator : AbstractValidator<UpdateCreditTermCommand>
{
    public UpdateCreditTermCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DueDays).GreaterThanOrEqualTo(0);
    }
}
