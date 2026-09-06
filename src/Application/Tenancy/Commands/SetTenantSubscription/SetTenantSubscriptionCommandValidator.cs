using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.SetTenantSubscription;

public sealed class SetTenantSubscriptionCommandValidator : AbstractValidator<SetTenantSubscriptionCommand>
{
    public SetTenantSubscriptionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.PlanName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EndsAt).NotEmpty();
    }
}
