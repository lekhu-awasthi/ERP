using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Posting;

/// <summary>
/// Reads the currency and rate each allocation target was <b>booked into the general ledger at</b>
/// -- the one input <see cref="PaymentForexCalculator"/> cannot get from the payment itself.
///
/// <para>Phase 28 wrote this as a private method on <c>ApprovePaymentCommandHandler</c>. Phase 36
/// needs the identical read on the further-allocation path
/// (<c>ApplyPaymentAllocationCommandHandler</c>), and two settlement paths that disagreed about the
/// rate a document was booked at would post two different forex figures for the same pair of
/// documents. Both now agree by construction, which is phase 26b's rule about shared readers
/// applied to a posting input rather than to a report.</para>
///
/// <para>Batched per document type rather than per row, the shape <c>GlSourceDocumentResolver</c>
/// uses for the eleven GL-posting types (phase 26a). Only Invoice and PurchaseBill can be
/// allocation targets, so there are exactly two queries and each is skipped when no allocation
/// names that type.</para>
/// </summary>
internal static class AllocationTargetRates
{
    public static async Task<IReadOnlyList<ForexAllocation>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<PaymentAllocation> allocations,
        CancellationToken cancellationToken)
    {
        return await LoadAsync(
            db, organizationId,
            allocations.Select(x => (x.TargetDocumentType, x.TargetDocumentId, x.Amount)).ToList(),
            cancellationToken);
    }

    public static async Task<IReadOnlyList<ForexAllocation>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        IReadOnlyList<(DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount)> allocations,
        CancellationToken cancellationToken)
    {
        if (allocations.Count == 0)
        {
            return [];
        }

        var invoiceIds = allocations.Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId).Distinct().ToList();
        var billIds = allocations.Where(x => x.TargetDocumentType == DocumentType.PurchaseBill)
            .Select(x => x.TargetDocumentId).Distinct().ToList();

        var invoiceRates = invoiceIds.Count == 0
            ? []
            : await db.Invoices
                .Where(x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CurrencyCode, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => (x.CurrencyCode, x.ExchangeRate), cancellationToken);

        var billRates = billIds.Count == 0
            ? []
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && billIds.Contains(x.Id))
                .Select(x => new { x.Id, x.CurrencyCode, x.ExchangeRate })
                .ToDictionaryAsync(x => x.Id, x => (x.CurrencyCode, x.ExchangeRate), cancellationToken);

        var result = new List<ForexAllocation>();
        foreach (var allocation in allocations)
        {
            var target = allocation.TargetDocumentType == DocumentType.Invoice
                ? invoiceRates.GetValueOrDefault(allocation.TargetDocumentId)
                : billRates.GetValueOrDefault(allocation.TargetDocumentId);

            // A target the caller could not read is one its own checks already rejected, so a
            // missing entry can only be a target type that carries no currency at all. Treating it
            // as base currency at rate 1 keeps the arithmetic total rather than throwing on a shape
            // that cannot reach here today.
            result.Add(new ForexAllocation(
                allocation.Amount,
                target.CurrencyCode ?? CurrencyCatalog.BaseCode,
                target.ExchangeRate == 0 ? ExchangeRates.BaseRate : target.ExchangeRate));
        }

        return result;
    }
}
