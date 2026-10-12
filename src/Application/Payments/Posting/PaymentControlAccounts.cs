using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Posting;

/// <summary>
/// Phase 87 -- a payment settles each document it is allocated to out of the control account that document recorded at
/// its approval: an invoice's Receivable, a purchase bill's Payable. A document from before phase 87 recorded none and
/// is settled out of today's default, as every payment settled it before.
///
/// <para><b>One account, one line, as before.</b> When every target sits where the tenant's default is (every tenant that
/// never changed the default, and every target from before phase 87), the entry is exactly the one
/// <see cref="PaymentPostingRule"/> has always built. Only targets on another account split the control leg, one line per
/// account, each part's base amount taken from a running total so the parts add up to the cash line to the paisa (phase
/// 65's rule), and each part carrying its own realised exchange difference on its own account.</para>
/// </summary>
internal static class PaymentControlAccounts
{
    /// <summary>The control account each target recorded, or null where it recorded none.</summary>
    public static async Task<IReadOnlyDictionary<(DocumentType Type, Guid Id), Guid?>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyCollection<(DocumentType Type, Guid Id)> targets,
        CancellationToken cancellationToken)
    {
        var invoiceIds = targets.Where(x => x.Type == DocumentType.Invoice).Select(x => x.Id).Distinct().ToList();
        var billIds = targets.Where(x => x.Type == DocumentType.PurchaseBill).Select(x => x.Id).Distinct().ToList();

        var invoices = invoiceIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.ReceivableAccountId, cancellationToken);
        var bills = billIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && billIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.PayableAccountId, cancellationToken);

        return targets.Distinct().ToDictionary(
            x => x,
            x => x.Type == DocumentType.Invoice ? invoices.GetValueOrDefault(x.Id)
                : x.Type == DocumentType.PurchaseBill ? bills.GetValueOrDefault(x.Id)
                : null);
    }

    /// <summary>
    /// The approval's lines when some target sits on another control account than <paramref name="defaultControl"/>, or
    /// null when none does (the caller then posts <see cref="PaymentPostingRule"/>'s one-account entry unchanged).
    /// </summary>
    public static async Task<IReadOnlyList<GlLineInput>?> SplitLinesAsync(
        IAppDbContext db,
        Guid organizationId,
        PaymentGlDraft payment,
        Guid defaultControl,
        CancellationToken cancellationToken)
    {
        var accountOf = await LoadAsync(
            db, organizationId,
            [.. payment.Allocations.Select(x => (x.TargetDocumentType, x.TargetDocumentId))],
            cancellationToken);

        Guid AccountOf(PaymentGlAllocation x) => accountOf[(x.TargetDocumentType, x.TargetDocumentId)] ?? defaultControl;

        if (payment.Allocations.All(x => AccountOf(x) == defaultControl))
        {
            return null;
        }

        // The unallocated rest is the default account's, with any allocation whose target sits there; the default part
        // goes first so its line is where a reader expects the payment's control leg.
        var rest = payment.Amount - payment.Allocations.Sum(x => x.Amount);
        var parts = payment.Allocations
            .GroupBy(AccountOf)
            .Select(g => (Account: g.Key, Allocations: g.ToList()))
            .OrderByDescending(x => x.Account == defaultControl)
            .ToList();
        if (rest > 0m && parts.All(x => x.Account != defaultControl))
        {
            parts.Insert(0, (defaultControl, []));
        }

        var received = payment.Direction == PaymentDirection.Received;
        var cashBase = ExchangeRates.ToBase(payment.Amount, payment.ExchangeRate);
        var lines = new List<GlLineInput>
        {
            received ? new GlLineInput(payment.AccountId, cashBase, 0) : new GlLineInput(payment.AccountId, 0, cashBase),
        };

        var running = 0m;
        var postedBase = 0m;
        foreach (var part in parts)
        {
            running += part.Allocations.Sum(x => x.Amount) + (part.Account == defaultControl ? rest : 0m);
            var partBase = ExchangeRates.ToBase(running, payment.ExchangeRate) - postedBase;
            postedBase += partBase;
            if (partBase != 0m)
            {
                lines.Add(received ? new GlLineInput(part.Account, 0, partBase) : new GlLineInput(part.Account, partBase, 0));
            }

            if (part.Allocations.Count == 0)
            {
                continue;
            }

            var forex = await PaymentForexCalculator.CalculateAsync(
                db, organizationId, payment.Direction, payment.CurrencyCode, payment.ExchangeRate,
                await AllocationTargetRates.LoadAsync(
                    db, organizationId,
                    [.. part.Allocations.Select(x => (x.TargetDocumentType, x.TargetDocumentId, x.Amount))],
                    cancellationToken),
                cancellationToken);
            if (forex is { Amount: > 0 })
            {
                lines.AddRange(PaymentPostingRule.ForexLines(part.Account, forex));
            }
        }

        return lines;
    }

    /// <summary>
    /// A later allocation's move: the share leaves the account the payment's unallocated part was posted to and joins the
    /// target's. Empty when the two are the same account, which is every case before phase 87 and every tenant that never
    /// changed its default.
    /// </summary>
    public static IReadOnlyList<GlLineInput> MoveLines(
        PaymentDirection direction, Guid restAccount, Guid targetAccount, decimal amount, decimal exchangeRate)
    {
        if (restAccount == targetAccount)
        {
            return [];
        }

        var moved = ExchangeRates.ToBase(amount, exchangeRate);
        return direction == PaymentDirection.Received
            ? [new GlLineInput(restAccount, moved, 0), new GlLineInput(targetAccount, 0, moved)]
            : [new GlLineInput(targetAccount, moved, 0), new GlLineInput(restAccount, 0, moved)];
    }
}
