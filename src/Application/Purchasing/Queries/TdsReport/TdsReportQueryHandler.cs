using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.TdsReport;

public sealed class TdsReportQueryHandler(IAppDbContext db) : IRequestHandler<TdsReportQuery, TdsReportDto>
{
    public async Task<TdsReportDto> Handle(TdsReportQuery request, CancellationToken cancellationToken)
    {
        var purchaseBills = await db.PurchaseBills
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                && x.TdsTypeId != null && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, TdsTypeId = x.TdsTypeId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var purchaseBillGrossAmounts = await db.PurchaseBillLines
            .Where(x => purchaseBills.Select(b => b.Id).Contains(x.PurchaseBillId))
            .GroupBy(x => x.PurchaseBillId)
            .Select(g => new { PurchaseBillId = g.Key, Gross = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.PurchaseBillId, x => x.Gross, cancellationToken);

        var expenses = await db.Expenses
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Approved
                && x.TdsTypeId != null && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, TdsTypeId = x.TdsTypeId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var expenseGrossAmounts = await db.ExpenseLines
            .Where(x => expenses.Select(e => e.Id).Contains(x.ExpenseId))
            .GroupBy(x => x.ExpenseId)
            .Select(g => new { ExpenseId = g.Key, Gross = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.ExpenseId, x => x.Gross, cancellationToken);

        var debitNotes = await db.DebitNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                && x.TdsTypeId != null && x.Date >= request.FromDate && x.Date <= request.ToDate)
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Date, TdsTypeId = x.TdsTypeId!.Value, x.TdsAmount })
            .ToListAsync(cancellationToken);
        var debitNoteGrossAmounts = await db.DebitNoteLines
            .Where(x => debitNotes.Select(d => d.Id).Contains(x.DebitNoteId))
            .GroupBy(x => x.DebitNoteId)
            .Select(g => new { DebitNoteId = g.Key, Gross = g.Sum(x => x.Amount + x.VatAmount) })
            .ToDictionaryAsync(x => x.DebitNoteId, x => x.Gross, cancellationToken);

        var contactIds = purchaseBills.Select(x => x.ContactId)
            .Concat(expenses.Select(x => x.ContactId))
            .Concat(debitNotes.Select(x => x.ContactId))
            .Distinct()
            .ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.Pan })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var tdsTypeIds = purchaseBills.Select(x => x.TdsTypeId)
            .Concat(expenses.Select(x => x.TdsTypeId))
            .Concat(debitNotes.Select(x => x.TdsTypeId))
            .Distinct()
            .ToList();
        var tdsTypes = await db.TdsTypes
            .Where(x => x.OrganizationId == request.OrganizationId && tdsTypeIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.RatePct })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = new List<TdsReportRowDto>();

        foreach (var bill in purchaseBills)
        {
            var contact = contacts[bill.ContactId];
            var tdsType = tdsTypes[bill.TdsTypeId];
            rows.Add(new TdsReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, DocumentType.PurchaseBill,
                bill.Code, bill.Date, tdsType.Code, tdsType.Name, tdsType.RatePct,
                purchaseBillGrossAmounts.GetValueOrDefault(bill.Id), bill.TdsAmount));
        }

        foreach (var expense in expenses)
        {
            var contact = contacts[expense.ContactId];
            var tdsType = tdsTypes[expense.TdsTypeId];
            rows.Add(new TdsReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, DocumentType.Expense,
                expense.Code, expense.Date, tdsType.Code, tdsType.Name, tdsType.RatePct,
                expenseGrossAmounts.GetValueOrDefault(expense.Id), expense.TdsAmount));
        }

        foreach (var debitNote in debitNotes)
        {
            var contact = contacts[debitNote.ContactId];
            var tdsType = tdsTypes[debitNote.TdsTypeId];
            rows.Add(new TdsReportRowDto(
                contact.Id, contact.Code, contact.Name, contact.Pan, DocumentType.DebitNote,
                debitNote.Code, debitNote.Date, tdsType.Code, tdsType.Name, tdsType.RatePct,
                -debitNoteGrossAmounts.GetValueOrDefault(debitNote.Id), -debitNote.TdsAmount));
        }

        var orderedRows = rows.OrderBy(x => x.EntryDate).ThenBy(x => x.EntryNo).ToList();

        return new TdsReportDto(request.FromDate, request.ToDate, orderedRows);
    }
}
