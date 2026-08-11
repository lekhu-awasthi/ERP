using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateCreditTerm;

public sealed class CreateCreditTermCommandValidator : AbstractValidator<CreateCreditTermCommand>
{
    public CreateCreditTermCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DueDays).GreaterThanOrEqualTo(0);
    }
}
