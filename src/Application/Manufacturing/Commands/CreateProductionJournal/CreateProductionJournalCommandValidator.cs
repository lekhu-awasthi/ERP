using FluentValidation;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;

public sealed class CreateProductionJournalCommandValidator : AbstractValidator<CreateProductionJournalCommand>
{
    public CreateProductionJournalCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OutputQuantity).GreaterThan(0);
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.RawMaterials).NotNull().Must(x => x is { Count: > 0 })
            .WithMessage("A production journal needs at least one raw material.");
        RuleFor(x => x.ByProducts).NotNull();
        RuleFor(x => x.Expenses).NotNull();
        this.ValidateProductionLines(x => x.RawMaterials, x => x.ByProducts, x => x.Expenses);
    }
}
