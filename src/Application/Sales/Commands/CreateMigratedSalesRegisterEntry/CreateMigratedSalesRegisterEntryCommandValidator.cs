using FluentValidation;

namespace ErpApp.Application.Sales.Commands.CreateMigratedSalesRegisterEntry;

/// <summary>
/// <para><b>No amount is constrained to be non-negative</b>, unlike every document validator in this
/// tree. A migrated sales <i>return</i> is a negative row (see the aggregate's doc comment), exactly
/// as the live Sales Register renders a CreditNote, so a negative total is the correct
/// representation rather than an error.</para>
///
/// <para><b>Nor is VAT cross-checked against the taxable value.</b> Requiring
/// VatAmount == TaxableValue * 0.13 is tempting and would be wrong: a prior system's register
/// carries whatever was actually filed, rounding included. Silently "correcting" a filed number, or
/// rejecting it, would make the migrated register disagree with the return the tenant has already
/// submitted to the IRD -- the one thing this feature must never do. The template's instructions say
/// the values are copied verbatim.</para>
/// </summary>
public sealed class CreateMigratedSalesRegisterEntryCommandValidator
    : AbstractValidator<CreateMigratedSalesRegisterEntryCommand>
{
    public CreateMigratedSalesRegisterEntryCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PartyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartyPan).MaximumLength(20);
        RuleFor(x => x.ExportCountry).MaximumLength(100);
        RuleFor(x => x.ExportDeclarationNo).MaximumLength(50);
    }
}
