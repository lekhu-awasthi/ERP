using System.Linq.Expressions;
using ErpApp.Domain.Sales;
using FluentValidation;

namespace ErpApp.Application.Sales;

/// <summary>
/// Phase 69 -- the shape of a credit note's invoice reference and reason, shared by the Create and Update
/// validators so each gets a 400 naming the field rather than the aggregate's 500 (phase 39's division of
/// labour). The checks that need the database -- the invoice's customer, currency, location, date and
/// remaining value, and a typed date being older than the first invoice -- are the handler's
/// (<see cref="CreditNoteInvoiceReferences"/>).
///
/// <para>Every rule is built from an <see cref="Expression{TDelegate}"/>, never a captured
/// <see cref="Func{T, TResult}"/>: a <c>RuleFor</c> over a delegate cannot infer its property name and
/// 500s every endpoint it guards (phase 25). The compiled delegates are used only inside <c>When</c>.</para>
/// </summary>
public static class CreditNoteInvoiceReferenceRules
{
    public static void AddInvoiceReferenceRules<TCommand>(
        this AbstractValidator<TCommand> validator,
        Expression<Func<TCommand, Guid?>> invoiceId,
        Expression<Func<TCommand, string?>> invoiceNumber,
        Expression<Func<TCommand, DateOnly?>> invoiceDate,
        Expression<Func<TCommand, string?>> reason)
    {
        var getInvoiceId = invoiceId.Compile();
        var getNumber = invoiceNumber.Compile();
        var getDate = invoiceDate.Compile();

        validator.RuleFor(invoiceId)
            .NotEqual(Guid.Empty)
            .When(x => getInvoiceId(x) is not null)
            .WithMessage("Choose an invoice from the list.");

        validator.RuleFor(invoiceId)
            .Null()
            .When(x => !string.IsNullOrWhiteSpace(getNumber(x)) || getDate(x) is not null)
            .WithMessage("Name either an invoice from the list or one issued before this system, not both.");

        validator.RuleFor(invoiceNumber)
            .MaximumLength(CreditNote.MaxAgainstInvoiceNumberLength);

        validator.RuleFor(invoiceNumber)
            .NotEmpty()
            .When(x => getDate(x) is not null)
            .WithMessage("Type the invoice's number as well as its date.");

        validator.RuleFor(invoiceDate)
            .NotNull()
            .When(x => !string.IsNullOrWhiteSpace(getNumber(x)))
            .WithMessage("Type the invoice's date as well as its number.");

        validator.RuleFor(reason)
            .MaximumLength(CreditNote.MaxReasonLength);
    }
}
