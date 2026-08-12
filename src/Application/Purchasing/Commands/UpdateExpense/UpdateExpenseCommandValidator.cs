using FluentValidation;

namespace ErpApp.Application.Purchasing.Commands.UpdateExpense;

public sealed class UpdateExpenseCommandValidator : AbstractValidator<UpdateExpenseCommand>
{
    public UpdateExpenseCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.SupplierInvoiceReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.AccountId).NotEmpty();
            line.RuleFor(x => x.Amount).GreaterThan(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
        });
    }
}
