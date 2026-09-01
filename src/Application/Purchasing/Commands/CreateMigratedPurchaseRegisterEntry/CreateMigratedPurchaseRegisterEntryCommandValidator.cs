using FluentValidation;

namespace ErpApp.Application.Purchasing.Commands.CreateMigratedPurchaseRegisterEntry;

/// <summary>Same two deliberate omissions as the Sales-side validator: amounts may be negative (a
/// migrated purchase return is a negative row) and VAT is never cross-checked against the taxable
/// value (a filed figure is copied verbatim, rounding included).</summary>
public sealed class CreateMigratedPurchaseRegisterEntryCommandValidator
    : AbstractValidator<CreateMigratedPurchaseRegisterEntryCommand>
{
    public CreateMigratedPurchaseRegisterEntryCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ImportDeclarationNo).MaximumLength(50);
        RuleFor(x => x.PartyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartyPan).MaximumLength(20);
    }
}
