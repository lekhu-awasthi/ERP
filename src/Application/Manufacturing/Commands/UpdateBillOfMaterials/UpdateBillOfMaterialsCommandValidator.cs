using FluentValidation;

namespace ErpApp.Application.Manufacturing.Commands.UpdateBillOfMaterials;

public sealed class UpdateBillOfMaterialsCommandValidator : AbstractValidator<UpdateBillOfMaterialsCommand>
{
    public UpdateBillOfMaterialsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OutputQuantity).GreaterThan(0);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.RawMaterials).NotNull().Must(x => x is { Count: > 0 })
            .WithMessage("A bill of materials needs at least one raw material.");
        RuleFor(x => x.ByProducts).NotNull();
        RuleFor(x => x.Expenses).NotNull();
        this.ValidateProductionLines(x => x.RawMaterials, x => x.ByProducts, x => x.Expenses);
    }
}
