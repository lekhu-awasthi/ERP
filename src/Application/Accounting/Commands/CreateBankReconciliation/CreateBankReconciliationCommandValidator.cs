using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateBankReconciliation;

/// <summary>
/// The half of the rule a validator can see: both sides are named and non-empty, and no id is
/// repeated. The other half — that the two sides total the same figure — needs the rows themselves
/// and is raised in the handler, which is where the reason is written down.
/// </summary>
public sealed class CreateBankReconciliationCommandValidator
    : AbstractValidator<CreateBankReconciliationCommand>
{
    public CreateBankReconciliationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();

        RuleFor(x => x.StatementLineIds)
            .NotNull()
            .Must(x => x is { Count: > 0 })
            .WithMessage("Select at least one bank statement line to reconcile.");

        RuleFor(x => x.GlLineIds)
            .NotNull()
            .Must(x => x is { Count: > 0 })
            .WithMessage("Select at least one book transaction to reconcile.");

        // A repeated id would be counted twice by the sum and once by the update, so a caller could
        // "balance" a selection against itself. Rejecting it is cheaper than de-duplicating
        // silently, which would reconcile something other than what was asked for.
        RuleFor(x => x.StatementLineIds)
            .Must(NoDuplicates)
            .When(x => x.StatementLineIds is not null)
            .WithMessage("The same bank statement line was selected more than once.");

        RuleFor(x => x.GlLineIds)
            .Must(NoDuplicates)
            .When(x => x.GlLineIds is not null)
            .WithMessage("The same book transaction was selected more than once.");
    }

    private static bool NoDuplicates(IReadOnlyList<Guid> ids) => ids.Distinct().Count() == ids.Count;
}
