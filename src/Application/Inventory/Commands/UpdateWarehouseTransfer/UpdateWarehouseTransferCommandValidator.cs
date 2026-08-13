using FluentValidation;

namespace ErpApp.Application.Inventory.Commands.UpdateWarehouseTransfer;

public sealed class UpdateWarehouseTransferCommandValidator : AbstractValidator<UpdateWarehouseTransferCommand>
{
    public UpdateWarehouseTransferCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.FromWarehouseId).NotEmpty();
        RuleFor(x => x.ToWarehouseId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
