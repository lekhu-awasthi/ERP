using FluentValidation;

namespace ErpApp.Application.Crm.Commands.AdjustSmsCredit;

public sealed class AdjustSmsCreditCommandValidator : AbstractValidator<AdjustSmsCreditCommand>
{
    public AdjustSmsCreditCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ChangeAmount).NotEqual(0);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
