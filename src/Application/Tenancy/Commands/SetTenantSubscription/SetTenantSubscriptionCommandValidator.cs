using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

public sealed class SetTenantSubscriptionCommandValidator : AbstractValidator<SetTenantSubscriptionCommand>
{
    public SetTenantSubscriptionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EndsAt).NotEmpty();

        // The Domain rejects these too, and must: the aggregate is the backstop. But a Domain
        // invariant reached through an endpoint is a 500, which tells a caller nothing -- phase 39's
        // lesson. Stated here as well, each naming its own field, so the API answers 400.
        RuleFor(x => x.SubscriptionAmount)
            .GreaterThanOrEqualTo(0)
            .When(x => x.SubscriptionAmount.HasValue)
            .WithMessage("A subscription amount cannot be negative.");

        RuleFor(x => x.ProductQuota)
            .GreaterThanOrEqualTo(0)
            .When(x => x.ProductQuota.HasValue)
            .WithMessage("A product quota cannot be negative; zero means not metered.");

        RuleFor(x => x.TransactionQuota)
            .GreaterThanOrEqualTo(0)
            .When(x => x.TransactionQuota.HasValue)
            .WithMessage("A transaction quota cannot be negative; zero means not metered.");
    }
}
