using FluentValidation;

namespace ErpApp.Application.Payments.Queries.GetDefaultPaymentAllocations;

public sealed class GetDefaultPaymentAllocationsQueryValidator : AbstractValidator<GetDefaultPaymentAllocationsQuery>
{
    public GetDefaultPaymentAllocationsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
    }
}
