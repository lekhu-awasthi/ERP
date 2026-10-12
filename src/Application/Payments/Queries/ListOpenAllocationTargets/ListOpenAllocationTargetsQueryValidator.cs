using FluentValidation;

namespace ErpApp.Application.Payments.Queries.ListOpenAllocationTargets;

public sealed class ListOpenAllocationTargetsQueryValidator : AbstractValidator<ListOpenAllocationTargetsQuery>
{
    public ListOpenAllocationTargetsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
    }
}
