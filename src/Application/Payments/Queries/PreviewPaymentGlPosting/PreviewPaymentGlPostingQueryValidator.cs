using ErpApp.Application.Common.Currencies;
using FluentValidation;

namespace ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;

/// <summary>Phase 88 -- the same rules Create applies to the fields the preview now carries, plus the one Approve
/// enforces (allocations within the amount) as a 400 naming the field, so a form mid-edit is told what is wrong.</summary>
public sealed class PreviewPaymentGlPostingQueryValidator : AbstractValidator<PreviewPaymentGlPostingQuery>
{
    public PreviewPaymentGlPostingQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(allocation =>
        {
            allocation.RuleFor(x => x.TargetDocumentId).NotEmpty();
            allocation.RuleFor(x => x.Amount).GreaterThan(0);
        });
        RuleFor(x => x.Allocations)
            .Must((query, allocations) => allocations.Sum(x => x.Amount) <= query.Amount)
            .When(x => x.Allocations is not null)
            .WithMessage("A payment's allocations cannot exceed its Amount.");

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);
    }
}
