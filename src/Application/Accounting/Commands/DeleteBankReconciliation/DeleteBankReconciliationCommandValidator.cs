using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.DeleteBankReconciliation;

public sealed class DeleteBankReconciliationCommandValidator
    : AbstractValidator<DeleteBankReconciliationCommand>
{
    public DeleteBankReconciliationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();
        RuleFor(x => x.ReconciliationId).NotEmpty();
    }
}
