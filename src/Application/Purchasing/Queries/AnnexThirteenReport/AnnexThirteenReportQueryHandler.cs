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
            .Select(x => new { x.Id, x.ContactId, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var invoiceLines = await db.InvoiceLines
            .Where(x => invoices.Select(i => i.Id).Contains(x.InvoiceId))
            // Phase 61 -- a till line's service charge is part of what was charged for the supply
            // (inside the VAT base), so it counts toward the party's gross. Zero on every ERP line.
            .Select(x => new { x.InvoiceId, x.ProductId, Amount = x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount })
            .ToListAsync(cancellationToken);

        var creditNotes = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var creditNoteLines = await db.CreditNoteLines
            .Where(x => creditNotes.Select(c => c.Id).Contains(x.CreditNoteId))
            // Phase 63 -- the mirror of the invoice line above: a till refund gives back its service charge.
            .Select(x => new { x.CreditNoteId, x.ProductId, Amount = x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount })
            .ToListAsync(cancellationToken);

        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var purchaseBillLines = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Amount, x.ExpenditureClassification })
            .ToListAsync(cancellationToken);

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var expenseLines = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .Select(x => new { x.ExpenseId, x.Amount })
            .ToListAsync(cancellationToken);

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.ReferrerType, x.ReferrerId, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var debitNoteLines = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Rate, x.VatRate, x.Amount })
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

        // Phase 44 (43 Decision D) -- <b>the annex is filed in NPR, so it reports NPR.</b> Every one
        // of the five accumulation loops below folds its line at <i>its own document's</i> rate
        // before the line reaches a bucket, which is the same rule the Purchase Register and the VAT
        // Summary now follow: a contact's row here is a sum across documents, so two documents in
        // two currencies have to be made commensurable before they are added to each other.
        //
        // Phase 90 -- <b>every figure is before VAT</b>, as the reference product's annex is (read
        // 2026-10-13: an expense of 1,000 at 13% plus 500 without VAT counts 1,500, and a customer's
        // sales net of returns count 707.05 of 798.97 billed). Annex 13 is filed with the income-tax
        // return, where a VAT-registered business's purchases and sales exclude VAT, so the line's VAT
        // is not read at all and the 1-lakh threshold is measured against the value alone. Before
        // this phase the annex added each line's VAT, which put every figure 13% high and crossed the
        // threshold early. Folded per line at its own document's rate, so a line here is exactly the
        // value the Purchase or Sales Register reports for it.
        static decimal ToBase(decimal amount, decimal exchangeRate) => ExchangeRates.ToBase(amount, exchangeRate);

        var invoicesById = invoices.ToDictionary(x => x.Id);
        foreach (var line in invoiceLines)
        {
            var invoice = invoicesById[line.InvoiceId];
            var contactId = invoice.ContactId;
            var value = ToBase(line.Amount, invoice.ExchangeRate);
            Accumulate(productTypes[line.ProductId] == ProductType.Goods ? goodsSales : serviceSales, contactId, value);
        }

        var creditNotesById = creditNotes.ToDictionary(x => x.Id);
        foreach (var line in creditNoteLines)
        {
            var creditNote = creditNotesById[line.CreditNoteId];
            var contactId = creditNote.ContactId;
            var value = ToBase(line.Amount, creditNote.ExchangeRate);
            Accumulate(productTypes[line.ProductId] == ProductType.Goods ? goodsSales : serviceSales, contactId, -value);
        }

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);
        foreach (var line in purchaseBillLines)
        {
            var purchaseBill = purchaseBillsById[line.PurchaseBillId];
            var contactId = purchaseBill.ContactId;
            var value = ToBase(line.Amount, purchaseBill.ExchangeRate);
            var bucket = (productTypes[line.ProductId], line.ExpenditureClassification) switch
            {
                (ProductType.Goods, ExpenditureClassification.Capital) => goodsPurchaseCapital,
                (ProductType.Goods, ExpenditureClassification.Others) => goodsPurchaseOthers,
                (ProductType.Service, ExpenditureClassification.Capital) => servicePurchaseCapital,
                _ => servicePurchaseOthers,
            };
            Accumulate(bucket, contactId, value);
        }

        var expensesById = expenses.ToDictionary(x => x.Id);
        foreach (var line in expenseLines)
        {
            // Expense carries no ProductId (account-based lines) and no ExpenditureClassification --
            // bucketed as Service/Others: Expense is inherently non-goods (no inventory, no
            // Quantity) and "Others" is ExpenditureClassification's own documented default.
            var expense = expensesById[line.ExpenseId];
            Accumulate(servicePurchaseOthers, expense.ContactId, ToBase(line.Amount, expense.ExchangeRate));
        }

        foreach (var line in debitNoteLines)
        {
            var debitNote = debitNotesById[line.DebitNoteId];
            var value = ToBase(line.Amount, debitNote.ExchangeRate);

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
            Accumulate(bucket, debitNote.ContactId, -value);
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
