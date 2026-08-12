using FluentValidation;

namespace ErpApp.Application.Sales.Commands.UpdateInvoice;

public sealed class UpdateInvoiceCommandValidator : AbstractValidator<UpdateInvoiceCommand>
{
    public UpdateInvoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
        });
    }
}
