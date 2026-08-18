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
    }
}
