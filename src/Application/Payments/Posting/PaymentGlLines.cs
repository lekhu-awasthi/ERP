using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;

namespace ErpApp.Application.Payments.Posting;

/// <summary>One allocation as the posting reads it: the target and the amount, in the payment's currency.</summary>
internal sealed record PaymentGlAllocation(DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount);

/// <summary>
/// Everything a payment's approval entry is built from. Approve reads it off the saved <see cref="Payment"/>; the
/// preview reads it off the form the user is holding, which may never have been saved.
/// </summary>
internal sealed record PaymentGlDraft(
    Guid AccountId,
    decimal Amount,
    PaymentDirection Direction,
    string CurrencyCode,
    decimal ExchangeRate,
    IReadOnlyList<PaymentGlAllocation> Allocations)
{
    public static PaymentGlDraft Of(Payment payment) => new(
        payment.AccountId, payment.Amount, payment.Direction, payment.CurrencyCode, payment.ExchangeRate,
        [.. payment.Allocations.Select(x => new PaymentGlAllocation(x.TargetDocumentType, x.TargetDocumentId, x.Amount))]);
}

/// <summary>The entry, and where its unallocated part sits (today's default control account).</summary>
internal sealed record PaymentGlPlan(Guid UnallocatedControlAccountId, IReadOnlyList<GlLineInput> Lines);

/// <summary>
/// Phase 88 -- <b>the one place a payment's approval entry is built</b>, for Approve and for the form's preview alike.
///
/// <para>Until this phase the preview built its own entry from the cash account, the amount and the direction only:
/// no currency, so a foreign payment's preview showed its transaction amount as if it were base currency; no
/// allocations, so it never showed a realised exchange difference, nor phase 87's split of the control leg across
/// the accounts the targets recorded. Approve could post an entry the preview had never shown. Both now call this,
/// so they cannot disagree.</para>
/// </summary>
internal static class PaymentGlLines
{
    public static async Task<PaymentGlPlan> BuildAsync(
        IAppDbContext db,
        IGlPostingRule<PaymentPostingInput> postingRule,
        Guid organizationId,
        PaymentGlDraft draft,
        CancellationToken cancellationToken)
    {
        // Phase 28: the payment's own Amount is in its transaction currency, so it converts here like every other
        // document's line amounts (see ExchangeRates). The realised exchange difference its allocations produce is a
        // separate, second leg -- computed below, from the rates the *target* documents were booked at.
        var postingInput = await PaymentAccountResolver.ResolveAsync(
            db, organizationId, draft.AccountId,
            ExchangeRates.ToBase(draft.Amount, draft.ExchangeRate), draft.Direction, cancellationToken);

        var forex = await PaymentForexCalculator.CalculateAsync(
            db, organizationId, draft.Direction, draft.CurrencyCode, draft.ExchangeRate,
            await AllocationTargetRates.LoadAsync(
                db, organizationId,
                [.. draft.Allocations.Select(x => (x.TargetDocumentType, x.TargetDocumentId, x.Amount))],
                cancellationToken),
            cancellationToken);

        if (forex is not null)
        {
            postingInput = postingInput with { Forex = forex };
        }

        // Phase 87 -- each allocated part settled out of the control account its target recorded. One account is the
        // entry it always was.
        var lines = await PaymentControlAccounts.SplitLinesAsync(
                db, organizationId, draft, postingInput.ControlAccountId, cancellationToken)
            ?? postingRule.BuildLines(postingInput);

        return new PaymentGlPlan(postingInput.ControlAccountId, lines);
    }
}
