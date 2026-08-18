using FluentValidation;

namespace ErpApp.Application.Crm.Commands.UpdateDeal;

public sealed class UpdateDealCommandValidator : AbstractValidator<UpdateDealCommand>
{
    public UpdateDealCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.ExpectedRevenue).GreaterThanOrEqualTo(0);
    }
}
