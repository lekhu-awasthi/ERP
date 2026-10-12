using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Reports;

/// <summary>
/// Phase 89 -- the purchase book's positive half: every approved Purchase Bill <b>and Expense</b> in a period, bucketed
/// into the seven statutory columns by <see cref="PurchaseReturnReader.Bucket"/>. Extracted from
/// <c>PurchaseRegisterQueryHandler</c> so the Purchase Register and the VAT return read the same documents through the
/// same fold and the same bucketing, as phase 26c did for the return half. A month of the return equals the register
/// run over that month by construction, and <c>VatReturnRegisterAgreementTests</c> holds them to it.
///
/// <para><b>An expense is a purchase.</b> Its VAT is input tax under a VAT bill, so the user ruled (2026-10-12) that the
/// return claims it and the register lists it, together, or the two would disagree. An expense line names an account,
/// never a product, and carries no <c>ExpenditureClassification</c> and no import flag, so it is Others and local:
/// taxable non-capital when it carries VAT, tax-exempt when it does not. Annex 13 has bucketed an expense as Service
/// Others since phase 8e for the same reason.</para>
/// </summary>
internal static class PurchaseBookReader
{
    internal sealed record PurchaseBookRow(
        Guid Id,
        DocumentType DocumentType,
        Guid ContactId,
        string Code,
        DateOnly Date,
        string? ImportDocumentNo,
        PurchaseReturnReader.Bucketed Buckets);

    internal static async Task<List<PurchaseBookRow>> LoadAsync(
        IAppDbContext db,
        Guid organizationId,
        DateOnly fromDate,
        DateOnly toDate,
        Guid? contactId,
        CancellationToken cancellationToken,
        Guid? locationId = null,
        IReadOnlyList<Guid>? reportLocations = null)
    {
        var billQuery = db.PurchaseBills.Where(x =>
            x.OrganizationId == organizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        var expenseQuery = db.Expenses.Where(x =>
            x.OrganizationId == organizationId && x.Status == ExpenseStatus.Approved
            && x.Date >= fromDate && x.Date <= toDate);
        if (contactId is { } filter)
        {
            billQuery = billQuery.Where(x => x.ContactId == filter);
            expenseQuery = expenseQuery.Where(x => x.ContactId == filter);
        }

        // Phase 44 -- the Billing Location filter and the caller's report scope, on both document types: each carries its
        // own LocationId (phase 32's seventeen).
        billQuery = billQuery.AtLocations(locationId, reportLocations);
        expenseQuery = expenseQuery.AtLocations(locationId, reportLocations);

        var bills = await billQuery
            // Phase 44 (43 Decision D) -- ExchangeRate is carried for the fold to base currency below.
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.IsImport, x.ImportDocumentNo, x.ExchangeRate })
            .ToListAsync(cancellationToken);

        // Phase 42 -- the lines are joined to the document *query*, never fetched for the list of ids read back out of it,
        // which would reach SQL Server as an OPENJSON parameter as long as the period.
        var billLines = await (
                from line in db.PurchaseBillLines
                join bill in billQuery on line.PurchaseBillId equals bill.Id
                select new { line.PurchaseBillId, line.Amount, line.VatAmount, line.ExpenditureClassification, bill.ExchangeRate })
            .ToListAsync(cancellationToken);

        var expenses = await expenseQuery
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, x.ExchangeRate })
            .ToListAsync(cancellationToken);
        var expenseLines = await (
                from line in db.ExpenseLines
                join expense in expenseQuery on line.ExpenseId equals expense.Id
                select new { line.ExpenseId, line.Amount, line.VatAmount, expense.ExchangeRate })
            .ToListAsync(cancellationToken);

        // Phase 44 (43 Decision D) -- <b>the book is filed in NPR, so it reports NPR.</b> Folded per line, before Bucket,
        // never on the finished buckets: Bucketed.Total is the sum of the other seven, so converting buckets independently
        // would let Total stop equalling its parts. In memory, because ExchangeRates.ToBase is a static call SQL Server
        // cannot translate and InMemory silently runs in C# (phase-34b).
        var billImport = bills.ToDictionary(x => x.Id, x => x.IsImport);
        var billBuckets = billLines
            .GroupBy(x => x.PurchaseBillId)
            .ToDictionary(
                g => g.Key,
                g => PurchaseReturnReader.Bucket(
                    g.Select(l => (
                        ExchangeRates.ToBase(l.Amount, l.ExchangeRate),
                        ExchangeRates.ToBase(l.VatAmount, l.ExchangeRate),
                        l.ExpenditureClassification)),
                    billImport[g.Key]));
        var expenseBuckets = expenseLines
            .GroupBy(x => x.ExpenseId)
            .ToDictionary(
                g => g.Key,
                g => PurchaseReturnReader.Bucket(
                    g.Select(l => (
                        ExchangeRates.ToBase(l.Amount, l.ExchangeRate),
                        ExchangeRates.ToBase(l.VatAmount, l.ExchangeRate),
                        ExpenditureClassification.Others)),
                    isImport: false));

        return
        [
            .. bills.Select(x => new PurchaseBookRow(
                x.Id, DocumentType.PurchaseBill, x.ContactId, x.Code, x.Date, x.ImportDocumentNo,
                billBuckets.GetValueOrDefault(x.Id) ?? PurchaseReturnReader.Bucketed.Empty)),
            .. expenses.Select(x => new PurchaseBookRow(
                x.Id, DocumentType.Expense, x.ContactId, x.Code, x.Date, null,
                expenseBuckets.GetValueOrDefault(x.Id) ?? PurchaseReturnReader.Bucketed.Empty)),
        ];
    }
}
