using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Readers;

/// <summary>
/// Phase 38's third category: money in and money out, one row per <c>Payment</c>.
///
/// <para><b>Why the ledger does not already cover this.</b> An approved payment posts GL lines, so
/// Ledger Transactions has the amounts -- against Bank/Cash and a control account, identified by a
/// source document id and nothing else. What lives only on the Payment is everything a human uses to
/// recognise it: its own document number, which contact it moved against, which payment mode, and
/// whether it was received or paid. A tenant reconciling an export against a bank statement needs
/// those and cannot derive them.</para>
///
/// <para><b>Direction is a column, not a sign.</b> Unlike the document categories, where a return is
/// the negative of a sale and summing the column is the point, "received" and "paid" are two
/// different flows against two different control accounts; adding them up is rarely what anybody
/// means, and signing them would quietly invite it.</para>
///
/// <para><b>Phase 77 -- the till's money too.</b> A till sale is paid by its own tenders and a till refund paid out by
/// its own payouts (phases 61 and 63), never by a <c>Payment</c>, so every rupee a till took or gave back was missing
/// from this sheet while the Ledger Transactions sheet had it as account lines. Each tender is a Received row and each
/// payout a Paid row, numbered by its document; the change a sale gave back is a Paid row of its own, in the mode of the
/// sale's cash tender (change only ever comes out of cash tendered). A <b>Source</b> column, appended so phase 38's
/// positions hold, says which kind of row each is. As with the sales sheet, the four sources are read separately and
/// merged in memory rather than in one set operation.</para>
/// </summary>
public sealed class PaymentExportReader(IAppDbContext db) : IExportCategoryReader
{
    public ExportCategory Category => ExportCategory.Payments;

    public string SheetName => "Payments";

    public bool IsDateFiltered => true;

    public IReadOnlyList<string> Headers { get; } =
    [
        "Document No",
        "Date",
        "Direction",
        "Status",
        "Contact",
        "Payment Mode",
        "Currency",
        "Amount",
        "Reference",
        "Source",
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var payments =
            from payment in db.Payments
            where payment.OrganizationId == organizationId
                  && (range.From == null || payment.Date >= range.From)
                  && (range.To == null || payment.Date <= range.To)
            join contact in db.Contacts on payment.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join mode in db.PaymentModes on payment.PaymentModeId equals mode.Id into modes
            from mode in modes.DefaultIfEmpty()
            orderby payment.Date, payment.Code
            select new MoneyRow(
                payment.Code, payment.Date, payment.Direction, payment.Status.ToString(),
                contact == null ? null : contact.Name, mode == null ? null : mode.Name,
                payment.CurrencyCode, payment.Amount, payment.Reference, "Payment");

        var tenders =
            from tender in db.InvoiceTenders
            join doc in db.Invoices on tender.InvoiceId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join mode in db.PaymentModes on tender.PaymentModeId equals mode.Id into modes
            from mode in modes.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new MoneyRow(
                doc.Code, doc.Date, PaymentDirection.Received, doc.Status.ToString(),
                contact == null ? null : contact.Name, mode == null ? null : mode.Name,
                doc.CurrencyCode, tender.Amount, doc.Reference, "Till Sale");

        var change =
            from doc in db.Invoices
            where doc.OrganizationId == organizationId
                  && doc.ChangeAmount > 0m
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new MoneyRow(
                doc.Code, doc.Date, PaymentDirection.Paid, doc.Status.ToString(),
                contact == null ? null : contact.Name,
                (from tender in db.InvoiceTenders
                 where tender.InvoiceId == doc.Id && tender.Kind == PaymentModeKind.Cash
                 join mode in db.PaymentModes on tender.PaymentModeId equals mode.Id
                 select mode.Name).FirstOrDefault(),
                doc.CurrencyCode, doc.ChangeAmount, doc.Reference, "Till Change");

        var payouts =
            from payout in db.CreditNotePayouts
            join doc in db.CreditNotes on payout.CreditNoteId equals doc.Id
            where doc.OrganizationId == organizationId
                  && (range.From == null || doc.Date >= range.From)
                  && (range.To == null || doc.Date <= range.To)
            join contact in db.Contacts on doc.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join mode in db.PaymentModes on payout.PaymentModeId equals mode.Id into modes
            from mode in modes.DefaultIfEmpty()
            orderby doc.Date, doc.Code
            select new MoneyRow(
                doc.Code, doc.Date, PaymentDirection.Paid, doc.Status.ToString(),
                contact == null ? null : contact.Name, mode == null ? null : mode.Name,
                doc.CurrencyCode, payout.Amount, doc.Reference, "Till Refund");

        var totalRowCount = 0;
        var merged = new List<MoneyRow>();

        foreach (var query in new[] { payments, tenders, change, payouts })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var rows = merged
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ThenBy(r => r.Direction)
            .Take(maxRows)
            .Select(p => new object?[]
            {
                p.Code,
                ExportCell.LocalDate(p.Date),
                p.Direction.ToString(),
                p.Status,
                p.ContactName,
                p.ModeName,
                p.CurrencyCode,
                p.Amount,
                p.Reference,
                p.Source,
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    private sealed record MoneyRow(
        string Code,
        DateOnly Date,
        PaymentDirection Direction,
        string Status,
        string? ContactName,
        string? ModeName,
        string CurrencyCode,
        decimal Amount,
        string? Reference,
        string Source);
}
