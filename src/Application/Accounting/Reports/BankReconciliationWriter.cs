using ErpApp.Application.Common.Exceptions;
using ErpApp.Domain.Accounting;

namespace ErpApp.Application.Accounting.Reports;

/// <summary>
/// Phase 57 — the one place a <see cref="BankReconciliation"/> is created, and the rules that make
/// one legal.
///
/// <para><b>Why it exists.</b> Phase 56 had exactly one caller, so the checks lived inline in
/// <c>CreateBankReconciliationCommandHandler</c>. This phase adds a second: Quick Approve creates a
/// document from a statement line and then matches the two, which is a reconciliation arrived at by
/// a different route rather than a different kind of record. Phase 36's rule is that two paths agree
/// only through one shared implementation plus a test reading both — patching two copies into
/// agreement leaves a coincidence — and the rule this protects is the one the whole feature rests
/// on: <b>the two sides must total the same amount</b>.</para>
///
/// <para><b>It takes loaded rows and adds nothing to the context.</b> The caller owns the queries
/// (each has a different one: the command resolves ids the user ticked, Quick Approve knows its rows
/// already) and the caller owns <c>SaveChangesAsync</c>. What is shared is the part that must not
/// diverge: the already-reconciled refusal, the sum rule, and the mutation of both sides.</para>
///
/// <para><b>Quick Approve satisfies the sum rule by construction</b> — it builds a document for the
/// statement line's own amount — which is an argument for routing it through this check rather than
/// around it. A check that cannot fail today is what catches the change that makes it fail.</para>
/// </summary>
internal static class BankReconciliationWriter
{
    /// <summary>
    /// Builds the reconciliation and marks both sides as belonging to it. The returned aggregate is
    /// <b>not</b> added to the context — the caller does that, along with the save. The agreed total
    /// comes back with it because <see cref="BankReconciliation"/> deliberately stores no copy of
    /// it: the totals are a property of its members, and a stored second copy is the two-views
    /// divergence phase 37 ruled against.
    /// </summary>
    /// <exception cref="ConflictException">Any row already belongs to a reconciliation.</exception>
    /// <exception cref="FluentValidation.ValidationException">The two sides do not total the
    /// same.</exception>
    public static BankReconciliationWrite Reconcile(
        Guid organizationId,
        Guid bankAccountId,
        IReadOnlyList<BankStatementLine> statementLines,
        IReadOnlyList<GlLine> bookLines,
        Guid reconciledByUserId,
        DateTimeOffset now,
        string sumFailureField)
    {
        // Already-reconciled rows are a 409 naming the count, not a 404: the row exists and the
        // caller may well be looking at a stale pane after somebody else reconciled it. The
        // aggregate's Reconcile() would throw anyway -- this is here so the failure carries a
        // status code and a sentence rather than surfacing as a 500 (phase 39's rule: a Domain
        // invariant reached through an endpoint is a 500, which tells a caller nothing).
        var alreadyDone =
            statementLines.Count(x => x.ReconciliationId is not null)
            + bookLines.Count(x => x.ReconciliationId is not null);

        if (alreadyDone > 0)
        {
            throw new ConflictException(
                $"{alreadyDone} of the selected transactions are already reconciled. Reload the page "
                + "and select again.");
        }

        var statementTotal = BankMovementTotal.OfStatementLines(statementLines.Select(x => x.Amount));
        var bookTotal = BankMovementTotal.OfGlLines(bookLines);

        // The sum rule, raised here as a 400 that names a field rather than being left to the
        // aggregate's InvalidOperationException, which would reach the caller as a 500 saying
        // nothing (phase 39). A validator cannot make this check -- it needs both sets of rows --
        // so this is the earliest place it can be made, and the Domain check behind it stays as the
        // backstop that no other caller can get round. This is the same division the balanced-GL
        // rule already uses: JournalVoucher's validator gives the 400, GlJournalEntry.Post is the
        // invariant.
        if (statementTotal != bookTotal)
        {
            throw new FluentValidation.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    sumFailureField,
                    "The selected bank statement lines and book transactions must total the same "
                    + $"amount. The bank side totals {statementTotal.Signed:0.00} and the book side "
                    + $"totals {bookTotal.Signed:0.00}."),
            ]);
        }

        var reconciliation = BankReconciliation.Create(
            organizationId,
            bankAccountId,
            statementTotal,
            statementLines.Count,
            bookTotal,
            bookLines.Count,
            reconciledByUserId,
            now);

        foreach (var line in statementLines)
        {
            line.Reconcile(reconciliation.Id);
        }

        foreach (var line in bookLines)
        {
            line.Reconcile(reconciliation.Id);
        }

        return new BankReconciliationWrite(reconciliation, statementTotal);
    }
}

/// <param name="Total">The agreed total — equal on both sides by the rule above, so one value says
/// it for both.</param>
internal sealed record BankReconciliationWrite(BankReconciliation Reconciliation, BankMovementTotal Total);
