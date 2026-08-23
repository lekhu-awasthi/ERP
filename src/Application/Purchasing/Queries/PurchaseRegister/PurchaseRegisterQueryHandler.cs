using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.PurchaseRegister;

public sealed class PurchaseRegisterQueryHandler(IAppDbContext db) : IRequestHandler<PurchaseRegisterQuery, PurchaseRegisterDto>
{
    private sealed record Bucketed(
        decimal TaxExempt, decimal NonCapitalLocalValue, decimal NonCapitalLocalVat,
        decimal NonCapitalImportValue, decimal NonCapitalImportVat, decimal CapitalValue, decimal CapitalVat);

    public async Task<PurchaseRegisterDto> Handle(PurchaseRegisterQuery request, CancellationToken cancellationToken)
    {
        var rows = new List<PurchaseRegisterRowDto>();

        var purchaseBillQuery = db.PurchaseBills.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } purchaseBillContactId)
        {
            purchaseBillQuery = purchaseBillQuery.Where(x => x.ContactId == purchaseBillContactId);
        }

        var purchaseBills = await purchaseBillQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.Code, x.Date, x.SupplierInvoiceReference, x.IsImport, x.ImportDocumentNo,
            })
            .ToListAsync(cancellationToken);
        var purchaseBillIds = purchaseBills.Select(x => x.Id).ToList();
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.Amount, x.VatAmount, x.ExpenditureClassification, x.ProductId, x.Rate, x.VatRate })
            .ToListAsync(cancellationToken);

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);
        var purchaseBillBuckets = purchaseBillLines
            .GroupBy(x => x.PurchaseBillId)
            .ToDictionary(
                g => g.Key,
                g => BucketClassified(
                    g.Select(l => (l.Amount, l.VatAmount, l.ExpenditureClassification)), purchaseBillsById[g.Key].IsImport));

        var debitNoteQuery = db.DebitNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } debitNoteContactId)
        {
            debitNoteQuery = debitNoteQuery.Where(x => x.ContactId == debitNoteContactId);
        }

        var debitNotes = await debitNoteQuery
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.ReferrerType, x.ReferrerId })
            .ToListAsync(cancellationToken);
        var debitNoteIds = debitNotes.Select(x => x.Id).ToList();
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNoteIds.Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        // A DebitNoteLine carries no ExpenditureClassification/IsImport of its own -- resolve both
        // from the source PurchaseBill's matching line, same (ProductId, Rate, VatRate) key
        // AnnexThirteenReportQueryHandler already uses for the same reason. See phase-19-status.md.
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
        var debitNoteBuckets = debitNoteLines
            .GroupBy(x => x.DebitNoteId)
            .ToDictionary(g => g.Key, g =>
            {
                var debitNote = debitNotesById[g.Key];
                var sourcePurchaseBillId = debitNote.ReferrerType == DocumentType.PurchaseBill ? debitNote.ReferrerId : null;
                var isImport = sourcePurchaseBillId is { } id && referredPurchaseBills.GetValueOrDefault(id);

                var classified = g.Select(line =>
                {
                    var classification = ExpenditureClassification.Others;
                    if (sourcePurchaseBillId is { } pbId
                        && classificationBySourceLine.TryGetValue((pbId, line.ProductId, line.Rate, line.VatRate), out var sourceClassification))
                    {
                        classification = sourceClassification;
                    }
                    return (line.Amount, line.VatAmount, classification);
                });

                return BucketClassified(classified, isImport);
            });

        var contactIds = purchaseBills.Select(x => x.ContactId).Concat(debitNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        rows.AddRange(purchaseBills.Select(x =>
        {
            var b = purchaseBillBuckets.GetValueOrDefault(x.Id) ?? new Bucketed(0, 0, 0, 0, 0, 0, 0);
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, DocumentType.PurchaseBill, x.Code, x.ImportDocumentNo, x.ContactId, contact.Name, contact.Pan,
                b.TaxExempt, b.NonCapitalLocalValue, b.NonCapitalLocalVat,
                b.NonCapitalImportValue, b.NonCapitalImportVat, b.CapitalValue, b.CapitalVat);
        }));

        rows.AddRange(debitNotes.Select(x =>
        {
            var b = debitNoteBuckets.GetValueOrDefault(x.Id) ?? new Bucketed(0, 0, 0, 0, 0, 0, 0);
            var contact = contacts[x.ContactId];
            return new PurchaseRegisterRowDto(
                x.Date, DocumentType.DebitNote, x.Code, null, x.ContactId, contact.Name, contact.Pan,
                -b.TaxExempt, -b.NonCapitalLocalValue, -b.NonCapitalLocalVat,
                -b.NonCapitalImportValue, -b.NonCapitalImportVat, -b.CapitalValue, -b.CapitalVat);
        }));

        var orderedRows = rows.OrderBy(x => x.Date).ThenBy(x => x.DocumentCode).ToList();
        var paged = request.ExportAll ? orderedRows.ToUnpagedResult() : orderedRows.ToPagedResult(request.Page, request.PageSize);

        return new PurchaseRegisterDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount,
            orderedRows.Sum(x => x.TaxExemptValue),
            orderedRows.Sum(x => x.TaxableNonCapitalLocalValue), orderedRows.Sum(x => x.TaxableNonCapitalLocalVat),
            orderedRows.Sum(x => x.TaxableNonCapitalImportValue), orderedRows.Sum(x => x.TaxableNonCapitalImportVat),
            orderedRows.Sum(x => x.TaxableCapitalValue), orderedRows.Sum(x => x.TaxableCapitalVat));
    }

    private static Bucketed BucketClassified(IEnumerable<(decimal Amount, decimal VatAmount, ExpenditureClassification Classification)> lines, bool isImport)
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

        return new Bucketed(taxExempt, nonCapitalLocalValue, nonCapitalLocalVat, nonCapitalImportValue, nonCapitalImportVat, capitalValue, capitalVat);
    }
}
