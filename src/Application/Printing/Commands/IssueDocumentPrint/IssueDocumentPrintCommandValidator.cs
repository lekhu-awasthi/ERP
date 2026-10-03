using ErpApp.Application.Printing.Queries.PrintDocument;
using ErpApp.Domain.Sales;
using FluentValidation;

namespace ErpApp.Application.Printing.Commands.IssueDocumentPrint;

public sealed class IssueDocumentPrintCommandValidator : AbstractValidator<IssueDocumentPrintCommand>
{
    public IssueDocumentPrintCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();

        // Validation runs before AuthorizationBehavior, so this is also what keeps PermissionKey from
        // being asked of a type that has no View key at all.
        RuleFor(x => x.DocumentType)
            .Must(CountedPrints.Applies)
            .WithMessage("Only an invoice or a credit note is printed as a counted copy; other documents print with GET.");

        // The till receipt is the till's own command (PrintPosReceipt / PrintPosRefundReceipt).
        RuleFor(x => x.Medium)
            .Must(x => x is PrintMedium.Pdf or PrintMedium.Email)
            .WithMessage("A counted PDF is printed or emailed.");
    }
}
