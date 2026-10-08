using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Validation;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Sales.Commands.CreateCreditNote;

public sealed class CreateCreditNoteCommandValidator : AbstractValidator<CreateCreditNoteCommand>
{
    public CreateCreditNoteCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);

        // Phase 39: Terms is rich text and had no rule at all before this phase.
        RuleFor(x => x.Terms).RichText("Terms and conditions");
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);

        // Phase 69 -- the invoice the note relates to, and why.
        this.AddInvoiceReferenceRules(
            x => x.AgainstInvoiceId, x => x.AgainstInvoiceNumber, x => x.AgainstInvoiceDate, x => x.Reason);

        RuleFor(x => x.AgainstInvoiceId)
            .Null()
            .When(x => x.ReferrerType == DocumentType.Invoice)
            .WithMessage("A credit note converted from an invoice already names that invoice.");
        RuleFor(x => x.AgainstInvoiceNumber)
            .Empty()
            .When(x => x.ReferrerType == DocumentType.Invoice)
            .WithMessage("A credit note converted from an invoice already names that invoice.");
    }
}
