using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.QuickApproveBankStatementLine;

public sealed class QuickApproveBankStatementLineCommandValidator
    : AbstractValidator<QuickApproveBankStatementLineCommand>
{
    public QuickApproveBankStatementLineCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();
        RuleFor(x => x.StatementLineId).NotEmpty();

        // IsInEnum matters more here than on most enums: Target is what chooses between two
        // different documents, so an out-of-range value reaching the handler would fall through the
        // switch and read to the caller as "line not found" rather than as a bad request.
        RuleFor(x => x.Target).IsInEnum();

        RuleFor(x => x.TargetId)
            .NotEmpty()
            .WithMessage("Select the customer, supplier or account this transaction belongs to.");
    }
}
