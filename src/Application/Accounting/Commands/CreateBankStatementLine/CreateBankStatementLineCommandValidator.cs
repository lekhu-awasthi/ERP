using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateBankStatementLine;

/// <summary>
/// The rules are the reference product's own, read live rather than invented -- its validate leg
/// answers exactly three messages, and each maps to a rule here:
/// <list type="bullet">
/// <item><i>"Row [N]: invalid date"</i> -- a missing or unparseable date. Ours is
/// <c>ImportRowReader.GetRequiredDate</c>'s job at parse time, so by the time a command exists the
/// date is real; what is left is the sanity bound below.</item>
/// <item><i>"Row [N]: both deposit and withdrawal cannot be zero"</i>.</item>
/// <item><i>"Row [N]: amount must be either deposit or withdrawal"</i>.</item>
/// </list>
///
/// <para><b>One rule here is deliberately stricter than the reference product.</b> Its two-column
/// template accepts a <i>negative deposit</i> verbatim -- a row of -40 in the Deposit column came
/// back as <c>dr_amount: -40</c>, neither rejected nor folded into a withdrawal (probed live,
/// 2026-09-21). That is a defect, not a feature: it produces a statement line whose displayed
/// direction contradicts its sign, and phase 56 would then match it against a document of the
/// opposite sign. A column called Deposit takes a positive number, and the message says so.</para>
///
/// <para>These rules run in the dry run too, because phase 38's review resolves each row into its
/// command and then runs that command's own registered validators -- so the review shows these
/// exact messages rather than a second, weaker copy written for the importer.</para>
/// </summary>
public sealed class CreateBankStatementLineCommandValidator : AbstractValidator<CreateBankStatementLineCommand>
{
    public CreateBankStatementLineCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.BankAccountId).NotEmpty();

        RuleFor(x => x.Description).MaximumLength(500);

        RuleFor(x => x.Deposit)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Deposit must be a positive amount; enter money leaving the account as a withdrawal.");

        RuleFor(x => x.Withdrawal)
            .GreaterThanOrEqualTo(0m)
            .WithMessage("Withdrawal must be a positive amount; enter money entering the account as a deposit.");

        RuleFor(x => x.Deposit)
            .Must((command, _) => command.Deposit != 0m || command.Withdrawal != 0m)
            .WithMessage("A statement line must carry a deposit or a withdrawal.")
            .Must((command, _) => command.Deposit == 0m || command.Withdrawal == 0m)
            .WithMessage("A statement line is either a deposit or a withdrawal, not both.");

        // A bank cannot have moved money before there were banks, and a statement dated in the
        // next century is a mistyped year -- the one failure mode a date column has once it parses
        // at all. Deliberately not bounded to "not in the future": a statement is sometimes loaded
        // with a forward value date, and the reference product applies no future bound either.
        RuleFor(x => x.Date)
            .InclusiveBetween(new DateOnly(1900, 1, 1), new DateOnly(2999, 12, 31))
            .WithMessage("Date must be a real calendar date between 1900 and 2999.");
    }
}
