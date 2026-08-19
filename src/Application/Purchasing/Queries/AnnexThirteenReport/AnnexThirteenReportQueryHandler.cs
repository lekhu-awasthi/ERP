using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.AnnexThirteenReport;

public sealed class AnnexThirteenReportQueryHandler(IAppDbContext db)
    : IRequestHandler<AnnexThirteenReportQuery, AnnexThirteenReportDto>
{
    public async Task<AnnexThirteenReportDto> Handle(AnnexThirteenReportQuery request, CancellationToken cancellationToken)
    {
        var invoices = await db.Invoices
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            .Select(x => new { x.InvoiceId, x.ProductId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            .Select(x => new { x.CreditNoteId, x.ProductId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Amount, x.VatAmount, x.ExpenditureClassification })
            .ToListAsync(cancellationToken);

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.ReferrerType, x.ReferrerId })
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        // A DebitNoteLine carries no ExpenditureClassification of its own -- resolve it from the
        // source PurchaseBill's matching line via the exact (ProductId, Rate, VatRate) triple the
        // conversion-cap enforcement already keys on (PurchasingValidation.GetPurchaseBillRemainingByLineAsync).
        // A standalone DebitNote (no PurchaseBill referrer) defaults to Others, same as
        // PurchaseBillLine's own documented default.
        var referredPurchaseBillIds = debitNotes
            .Where(x => x.ReferrerType == DocumentType.PurchaseBill && x.ReferrerId is not null)
            .Select(x => x.ReferrerId!.Value)
            .Distinct()
            .ToList();
        var referredPurchaseBillLines = await db.PurchaseBillLines
            .Where(x => referredPurchaseBillIds.Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Rate, x.VatRate, x.ExpenditureClassification })
            .ToListAsync(cancellationToken);
        // GroupBy (not a raw ToDictionary) -- two lines on the same PurchaseBill can legitimately
        // share the same (ProductId, Rate, VatRate) key with different ExpenditureClassification
        // values (e.g. splitting one shipment's Capital portion from its Others portion); the first
        // one wins for classification-lookup purposes rather than crashing on a duplicate key.
        var classificationBySourceLine = referredPurchaseBillLines
            .GroupBy(x => (x.PurchaseBillId, x.ProductId, x.Rate, x.VatRate))
            .ToDictionary(g => g.Key, g => g.First().ExpenditureClassification);
        var debitNotesById = debitNotes.ToDictionary(x => x.Id);

        var productIds = invoiceLines.Select(x => x.ProductId)
            .Concat(creditNoteLines.Select(x => x.ProductId))
            .Concat(purchaseBillLines.Select(x => x.ProductId))
            .Concat(debitNoteLines.Select(x => x.ProductId))
            .Distinct()
            .ToList();
        var productTypes = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Type })
            .ToDictionaryAsync(x => x.Id, x => x.Type, cancellationToken);

        var contactIds = invoices.Select(x => x.ContactId)
            .Concat(creditNotes.Select(x => x.ContactId))
            .Concat(purchaseBills.Select(x => x.ContactId))
            .Concat(expenses.Select(x => x.ContactId))
            .Concat(debitNotes.Select(x => x.ContactId))
            .Distinct()
            .ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Pan, x.Name, x.Type, x.OpeningBalance })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var servicePurchaseCapital = new Dictionary<Guid, decimal>();
        var servicePurchaseOthers = new Dictionary<Guid, decimal>();
        var goodsPurchaseCapital = new Dictionary<Guid, decimal>();
        var goodsPurchaseOthers = new Dictionary<Guid, decimal>();
        var serviceSales = new Dictionary<Guid, decimal>();
        var goodsSales = new Dictionary<Guid, decimal>();

        static void Accumulate(Dictionary<Guid, decimal> bucket, Guid contactId, decimal amount) =>
            bucket[contactId] = bucket.GetValueOrDefault(contactId) + amount;

        var invoicesById = invoices.ToDictionary(x => x.Id);
        foreach (var line in invoiceLines)
        {
            var contactId = invoicesById[line.InvoiceId].ContactId;
            var gross = line.Amount + line.VatAmount;
            Accumulate(productTypes[line.ProductId] == ProductType.Goods ? goodsSales : serviceSales, contactId, gross);
        }

        var creditNotesById = creditNotes.ToDictionary(x => x.Id);
        foreach (var line in creditNoteLines)
        {
            var contactId = creditNotesById[line.CreditNoteId].ContactId;
            var gross = line.Amount + line.VatAmount;
            Accumulate(productTypes[line.ProductId] == ProductType.Goods ? goodsSales : serviceSales, contactId, -gross);
        }

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);
        foreach (var line in purchaseBillLines)
        {
            var contactId = purchaseBillsById[line.PurchaseBillId].ContactId;
            var gross = line.Amount + line.VatAmount;
            var bucket = (productTypes[line.ProductId], line.ExpenditureClassification) switch
            {
                (ProductType.Goods, ExpenditureClassification.Capital) => goodsPurchaseCapital,
                (ProductType.Goods, ExpenditureClassification.Others) => goodsPurchaseOthers,
                (ProductType.Service, ExpenditureClassification.Capital) => servicePurchaseCapital,
                _ => servicePurchaseOthers,
            };
            Accumulate(bucket, contactId, gross);
        }

        var expensesById = expenses.ToDictionary(x => x.Id);
        foreach (var line in expenseLines)
        {
            // Expense carries no ProductId (account-based lines) and no ExpenditureClassification --
            // bucketed as Service/Others: Expense is inherently non-goods (no inventory, no
            // Quantity) and "Others" is ExpenditureClassification's own documented default.
            var contactId = expensesById[line.ExpenseId].ContactId;
            Accumulate(servicePurchaseOthers, contactId, line.Amount + line.VatAmount);
        }

        foreach (var line in debitNoteLines)
        {
            var debitNote = debitNotesById[line.DebitNoteId];
            var gross = line.Amount + line.VatAmount;

            var classification = ExpenditureClassification.Others;
            if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } sourcePurchaseBillId
                && classificationBySourceLine.TryGetValue(
                    (sourcePurchaseBillId, line.ProductId, line.Rate, line.VatRate), out var sourceClassification))
            {
                classification = sourceClassification;
            }

            var bucket = (productTypes[line.ProductId], classification) switch
            {
                (ProductType.Goods, ExpenditureClassification.Capital) => goodsPurchaseCapital,
                (ProductType.Goods, ExpenditureClassification.Others) => goodsPurchaseOthers,
                (ProductType.Service, ExpenditureClassification.Capital) => servicePurchaseCapital,
                _ => servicePurchaseOthers,
            };
            Accumulate(bucket, debitNote.ContactId, -gross);
        }

        var rows = contactIds
            .Select(contactId =>
            {
                var contact = contacts[contactId];
                return new AnnexThirteenReportRowDto(
                    contact.Id, contact.Code, contact.Pan, contact.Name, contact.Type, contact.OpeningBalance,
                    servicePurchaseCapital.GetValueOrDefault(contactId),
                    servicePurchaseOthers.GetValueOrDefault(contactId),
                    goodsPurchaseCapital.GetValueOrDefault(contactId),
                    goodsPurchaseOthers.GetValueOrDefault(contactId),
                    serviceSales.GetValueOrDefault(contactId),
                    goodsSales.GetValueOrDefault(contactId));
            })
            .Where(row => row.TotalActivity >= request.ThresholdAmount)
            .OrderBy(row => row.ContactCode)
            .ToList();
        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new AnnexThirteenReportDto(
            request.FromDate, request.ToDate, request.ThresholdAmount, paged.Items,
            paged.Page, paged.PageSize, paged.TotalCount);
    }
}
