using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Posting;

/// <summary>One allocation, reduced to the two numbers the forex calculation needs.</summary>
internal sealed record ForexAllocation(decimal Amount, string TargetCurrencyCode, decimal TargetExchangeRate);

/// <summary>
/// Computes the realised exchange difference a payment's allocations produce, and resolves the
/// account it is booked to. This is <b>the</b> new posting rule of phase 28 -- everything else in
/// the phase is the fold that makes it possible.
///
/// <para><b>The arithmetic.</b> A document books its receivable (or payable) into the general
/// ledger at its own rate. A payment relieves that same receivable at the <i>payment's</i> rate.
/// When the two rates differ, relieving the control account at the payment's rate leaves a residual
/// balance behind that no later document will ever clear -- so the difference is written off to
/// profit and loss the moment it is realised, which is the instant the payment settles. Per
/// allocation:</para>
/// <code>
///   bookedBase   = ToBase(allocation.Amount, target document's rate)
///   settledBase  = ToBase(allocation.Amount, payment's rate)
///   difference   = bookedBase - settledBase
/// </code>
/// <para>For a <b>Received</b> payment the control account is Accounts Receivable, which the
/// invoice debited: a positive difference means fewer rupees arrived than were booked, so it is a
/// <b>loss</b>. For a <b>Paid</b> payment the control account is Accounts Payable, which the bill
/// credited: a positive difference means fewer rupees left than were booked, so it is a
/// <b>gain</b>. That single sign flip is the only thing that differs between the two directions,
/// and it is why this lives in one tested function rather than in two branches of a handler.</para>
///
/// <para><b>Netting.</b> Differences are summed across all of a payment's allocations before the
/// account is resolved, so a payment settling one invoice at a favourable rate and another at an
/// unfavourable one posts a single net line. That is correct at the control account (there is only
/// one, and only its net movement matters) and it is the conventional presentation in the P&amp;L.
/// A net of exactly zero posts nothing and requires no forex account to be configured.</para>
///
/// <para><b>Same-currency invariant.</b> An allocation whose target document is in a different
/// currency from the payment is rejected outright rather than converted. The allocation's Amount is
/// a single number with no currency of its own; treating it as the payment's currency while the
/// target booked it in another would silently over- or under-relieve that document's balance by the
/// whole exchange rate. Cross-currency settlement is a real feature in larger systems and it needs
/// two amounts, not one -- it is deliberately not in this phase's scope.</para>
///
/// <para><b>Not confirmed live.</b> The roadmap's decisive experiment -- add a currency on the
/// reference tenant, book a foreign invoice and a foreign receipt at different rates, read the GL
/// Transactions panel -- could not be run: that product's own "Add New Currency" catalog picker
/// returns "No data" on the UAT tenant, so no second currency can be activated and no
/// foreign-currency document can be created (2026-09-04). The shape above is therefore derived
/// from first principles, with one strong piece of live corroboration: that tenant's chart of
/// accounts carries a realised <i>Forex Gain</i> account under an Indirect Income group and no
/// unrealised or revaluation account of any kind, which is what a settlement-time realisation model
/// looks like and not what a period-end revaluation model looks like. Re-verify when the
/// reference product's catalog is fixed.</para>
/// </summary>
internal static class PaymentForexCalculator
{
    /// <summary>
    /// Returns the forex leg for a payment, or null when there is none. Reads nothing and resolves
    /// no account when <paramref name="paymentCurrencyCode"/> is the base currency -- the fast path
    /// every single-currency tenant takes.
    /// </summary>
    public static async Task<PaymentForexInput?> CalculateAsync(
        IAppDbContext db,
        Guid organizationId,
        PaymentDirection direction,
        string paymentCurrencyCode,
        decimal paymentExchangeRate,
        IReadOnlyList<ForexAllocation> allocations,
        CancellationToken cancellationToken)
    {
        if (allocations.Count == 0)
        {
            return null;
        }

        foreach (var allocation in allocations)
        {
            if (!string.Equals(allocation.TargetCurrencyCode, paymentCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    $"This payment is in {paymentCurrencyCode} but one of the documents it allocates to is in " +
                    $"{allocation.TargetCurrencyCode}. A payment can only be allocated to documents in its own currency.");
            }
        }

        var difference = allocations.Sum(x =>
            ExchangeRates.ToBase(x.Amount, x.TargetExchangeRate) - ExchangeRates.ToBase(x.Amount, paymentExchangeRate));

        if (difference == 0)
        {
            return null;
        }

        var signedGain = direction == PaymentDirection.Received ? -difference : difference;
        var forexAccountId = await ForexAccountResolver.ResolveAsync(db, organizationId, signedGain, cancellationToken);

        return new PaymentForexInput(forexAccountId, Math.Abs(difference), signedGain > 0);
    }
}
