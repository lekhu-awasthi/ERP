using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Exports;
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
    ];

    public async Task<ExportCategoryResult> ReadAsync(
        Guid organizationId, int maxRows, ExportDateRange range, CancellationToken cancellationToken)
    {
        var query =
            from payment in db.Payments
            where payment.OrganizationId == organizationId
                  && (range.From == null || payment.Date >= range.From)
                  && (range.To == null || payment.Date <= range.To)
            join contact in db.Contacts on payment.ContactId equals contact.Id into contacts
            from contact in contacts.DefaultIfEmpty()
            join mode in db.PaymentModes on payment.PaymentModeId equals mode.Id into modes
            from mode in modes.DefaultIfEmpty()
            orderby payment.Date, payment.Code
            select new
            {
                payment.Code,
                payment.Date,
                payment.Direction,
                payment.Status,
                ContactName = contact == null ? null : contact.Name,
                ModeName = mode == null ? null : mode.Name,
                payment.CurrencyCode,
                payment.Amount,
                payment.Reference,
            };

        var totalRowCount = await query.CountAsync(cancellationToken);
        var page = await query.Take(maxRows).ToListAsync(cancellationToken);

        var rows = page
            .Select(p => new object?[]
            {
                p.Code,
                ExportCell.LocalDate(p.Date),
                p.Direction.ToString(),
                p.Status.ToString(),
                p.ContactName,
                p.ModeName,
                p.CurrencyCode,
                p.Amount,
                p.Reference,
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }
}
