using FluentValidation;

namespace ErpApp.Application.Crm.Commands.CreateDeal;

public sealed class CreateDealCommandValidator : AbstractValidator<CreateDealCommand>
{
    public CreateDealCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.ExpectedRevenue).GreaterThanOrEqualTo(0);
        // Phase 39, found by the E2E: a body that omits this array binds it to null and the handler
        // dereferences it, so a caller's mistake is a 500 rather than a 400 naming the field. The
        // same shape phase 34b found by asking one question of every list -- a required member with
        // no rule at all. NotNull rather than NotEmpty: a deal with no assignee is legitimate.
        RuleFor(x => x.AssigneeUserIds)
            .NotNull()
            .WithMessage("Assignees must be supplied, as an empty list if the deal has none.");
    }
}
