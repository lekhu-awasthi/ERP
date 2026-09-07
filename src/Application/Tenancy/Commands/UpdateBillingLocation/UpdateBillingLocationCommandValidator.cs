using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.UpdateBillingLocation;

public sealed class UpdateBillingLocationCommandValidator : AbstractValidator<UpdateBillingLocationCommand>
{
    public UpdateBillingLocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        // Not NotEmpty, unlike the create validator: the seeded HeadOffice row has no address, and an
        // Admin renaming it or pointing it at a warehouse must not be forced to invent one first.
        RuleFor(x => x.Address).MaximumLength(250);
    }
}
