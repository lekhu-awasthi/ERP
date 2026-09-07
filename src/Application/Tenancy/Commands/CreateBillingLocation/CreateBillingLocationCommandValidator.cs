using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.CreateBillingLocation;

public sealed class CreateBillingLocationCommandValidator : AbstractValidator<CreateBillingLocationCommand>
{
    public CreateBillingLocationCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        // Required on the live dialog, so required here on the user-facing create path -- but the
        // aggregate keeps it nullable so the seeded HeadOffice row need not invent one. See
        // BillingLocation.Address.
        RuleFor(x => x.Address).NotEmpty().MaximumLength(250);
    }
}
