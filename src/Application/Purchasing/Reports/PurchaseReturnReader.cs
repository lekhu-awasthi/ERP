using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Reports;

/// <summary>
/// Every approved Debit Note in a period, bucketed into the seven statutory columns the Nepal IRD
/// purchase books use. Phase 26c extracted this from <c>PurchaseRegisterQueryHandler</c> so that the
/// <b>Purchase Register</b> and the new <b>Purchase Return Register</b> cannot disagree: the two
/// reports show the same debit notes -- negative in the first, positive in the second, confirmed
/// side by side on the live tenant on 2026-09-03 -- and the only honest way to guarantee the
/// magnitudes match is for one reader to produce them. That is phase-26b's
/// <c>ContactLedgerReader</c> rule, applied where it now has a second caller.
///
/// <para><b>Why the Purchase Return Register is not the Sales Return Register's mirror.</b> The
/// roadmap predicted a mirror; the live screen is not one. The sales-side return register carries
/// four money columns (Total / Tax-exempt / Taxable Value / Tax), while this one carries seven,
/// because it inherits the <i>Purchase</i> Register's Capital-versus-Others and Local-versus-Import
/// split. So the pair is two handlers, and 26b's "one handler discriminated by the side the route
/// hardcodes" pattern deliberately does not apply.</para>
///
/// <para><b>A DebitNoteLine carries no classification of its own.</b> Both
/// <c>ExpenditureClassification</c> and <c>IsImport</c> are resolved from the source Purchase
/// Bill's matching line, keyed by (PurchaseBillId, ProductId, Rate, VatRate) -- the same join
/// <c>AnnexThirteenReportQueryHandler</c> uses, for the same reason. A standalone debit note with no
/// referrer falls back to Others/local, which is what the register showed before this extraction.</para>
/// </summary>
internal static class PurchaseReturnReader
{
    /// <summary>
    /// One debit note's seven statutory magnitudes, always non-negative. The Purchase Register
    /// negates them at the point of rendering; the Return Register does not.
    /// </summary>
    internal sealed record Bucketed(
        decimal TaxExempt,
        decimal NonCapitalLocalValue,
        decimal NonCapitalLocalVat,
        decimal NonCapitalImportValue,
        decimal NonCapitalImportVat,
        decimal CapitalValue,
        decimal CapitalVat)
    {
        internal static Bucketed Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

        /// <summary>The register's जम्मा फिर्ता मूल्य (Total Return Value) column: every bucket added.</summary>
        public decimal Total =>
            TaxExempt + NonCapitalLocalValue + NonCapitalLocalVat
            + NonCapitalImportValue + NonCapitalImportVat + CapitalValue + CapitalVat;
    }

    internal sealed record DebitNoteRow(
        Guid Id,
        Guid ContactId,
        string Code,
        DateOnly Date,
        Bucketed Buckets);

    internal static async Task<List<DebitNoteRow>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? contactId,
        CancellationToken cancellationToken)
    {
        var query = db.DebitNotes.Where(x =>
            x.OrganizationId == organizationId && x.Status == DebitNoteStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        if (contactId is { } filter)
        {
            query = query.Where(x => x.ContactId == filter);
        }

        var debitNotes = await query
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.ReferrerType, x.ReferrerId })
            .ToListAsync(cancellationToken);
        if (debitNotes.Count == 0)
        {
            return [];
        }

        var debitNoteIds = debitNotes.Select(x => x.Id).ToList();
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNoteIds.Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var referredPurchaseBillIds = debitNotes
            .Where(x => x.ReferrerType == DocumentType.PurchaseBill && x.ReferrerId is not null)
            .Select(x => x.ReferrerId!.Value)
            .Distinct()
            .ToList();
        var referredPurchaseBills = await db.PurchaseBills
            .Where(x => referredPurchaseBillIds.Contains(x.Id))
            .Select(x => new { x.Id, x.IsImport })
            .ToDictionaryAsync(x => x.Id, x => x.IsImport, cancellationToken);
        var referredPurchaseBillLines = await db.PurchaseBillLines
            .Where(x => referredPurchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Rate, x.VatRate, x.ExpenditureClassification })
            .ToListAsync(cancellationToken);
        var classificationBySourceLine = referredPurchaseBillLines
            .GroupBy(x => (x.PurchaseBillId, x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => g.First().ExpenditureClassification);

        var debitNotesById = debitNotes.ToDictionary(x => x.Id);
        var buckets = debitNoteLines
            .GroupBy(x => x.DebitNoteId)
            .ToDictionary(g => g.Key, g =>
            {
                var debitNote = debitNotesById[g.Key];
                var sourcePurchaseBillId =
                    debitNote.ReferrerType == DocumentType.PurchaseBill ? debitNote.ReferrerId : null;
                var isImport = sourcePurchaseBillId is { } id && referredPurchaseBills.GetValueOrDefault(id);

                var classified = g.Select(line =>
                {
                    var classification = ExpenditureClassification.Others;
                    if (sourcePurchaseBillId is { } billId
                        && classificationBySourceLine.TryGetValue(
                            (billId, line.ProductId, line.Rate, line.VatRate), out var sourceClassification))
                    {
                        classification = sourceClassification;
                    }

                    return (line.Amount, line.VatAmount, classification);
                });

                return Bucket(classified, isImport);
            });

        return [.. debitNotes.Select(x => new DebitNoteRow(
            x.Id, x.ContactId, x.Code, x.Date, buckets.GetValueOrDefault(x.Id) ?? Bucketed.Empty))];
    }

    /// <summary>
    /// The statutory bucketing, shared with the Purchase Register's own bill rows. A zero-VAT line
    /// is tax-exempt whatever it was bought for; a Capital line goes to the capital column whether
    /// local or imported; everything else splits on the document's import flag.
    /// </summary>
    internal static Bucketed Bucket(
        IEnumerable<(decimal Amount, decimal VatAmount, ExpenditureClassification Classification)> lines,
        bool isImport)
    {
        decimal taxExempt = 0, nonCapitalLocalValue = 0, nonCapitalLocalVat = 0;
        decimal nonCapitalImportValue = 0, nonCapitalImportVat = 0, capitalValue = 0, capitalVat = 0;

        foreach (var (amount, vatAmount, classification) in lines)
        {
            if (vatAmount == 0)
            {
                taxExempt += amount;
            }
            else if (classification == ExpenditureClassification.Capital)
            {
                capitalValue += amount;
                capitalVat += vatAmount;
            }
            else if (isImport)
            {
                nonCapitalImportValue += amount;
                nonCapitalImportVat += vatAmount;
            }
            else
            {
                nonCapitalLocalValue += amount;
                nonCapitalLocalVat += vatAmount;
            }
        }

        return new Bucketed(
            taxExempt, nonCapitalLocalValue, nonCapitalLocalVat,
            nonCapitalImportValue, nonCapitalImportVat, capitalValue, capitalVat);
    }
}
