using FluentValidation;

namespace ErpApp.Application.Manufacturing.Queries.ProductionPlanning;

public sealed class ProductionPlanningQueryValidator : AbstractValidator<ProductionPlanningQuery>
{
    public ProductionPlanningQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}
