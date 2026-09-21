using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.DeleteBankStatementLines;

public sealed class DeleteBankStatementLinesCommandValidator
    : AbstractValidator<DeleteBankStatementLinesCommand>
{
    public DeleteBankStatementLinesCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();

        // Exactly one selector. Neither would delete the whole account's statement by accident --
        // the shape a delete endpoint must never have -- and both at once has no meaning the
        // caller could have intended.
        RuleFor(x => x)
            .Must(x => (x.LineIds is { Count: > 0 }) ^ (x.ImportJobId is not null))
            .WithName(nameof(DeleteBankStatementLinesCommand.LineIds))
            .WithMessage("Supply either the statement lines to delete or the import to undo, not both.");

        // A cap, because this is the one endpoint a client hands an unbounded id list to and
        // phase 42's measurement is that a materialised id list becomes an OPENJSON parameter as
        // long as the list. The list screen's own page size is well under this.
        RuleFor(x => x.LineIds!)
            .Must(ids => ids.Count <= 500)
            .When(x => x.LineIds is not null)
            .WithMessage("Delete at most 500 statement lines at a time.");

        RuleForEach(x => x.LineIds!)
            .NotEmpty()
            .When(x => x.LineIds is not null);
    }
}
