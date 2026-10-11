using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Exports;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Pos;
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
///
/// <para><b>Phase 78 -- the drawer's own cash in and out.</b> A Cash In (a float top-up, the owner's cash) is a Received
/// row and a Cash Out (vegetables paid from the till, a drop to the safe) a Paid row, numbered by its session, in the
/// mode of the location's cash tender, with its note as the Reference. A movement has no contact; what it moved against
/// is an account (phase 61 makes the cashier name one), so an appended <b>Account</b> column names it, and is empty on
/// every other row, whose other side is its contact. A movement posts when it is recorded and nothing voids it, so its
/// Status is Approved; its Date is the Nepal day it was recorded, and the period filter cuts on Nepal midnights (phase
/// 66). The opening float and a close's over/short are not rows: the float moves no money (it counts what is already
/// there) and the over/short corrects the drawer's account, which Ledger Transactions carries. <b>Exchange Rate</b> and
/// <b>Location</b> are appended as on the document sheets; a till's money is in the base currency.</para>
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
        "Exchange Rate",
        "Location",
        "Account",
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
                payment.CurrencyCode, payment.Amount, payment.Reference, "Payment",
                payment.ExchangeRate, payment.LocationId, null);

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
                doc.CurrencyCode, tender.Amount, doc.Reference, "Till Sale",
                doc.ExchangeRate, doc.LocationId, null);

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
                doc.CurrencyCode, doc.ChangeAmount, doc.Reference, "Till Change",
                doc.ExchangeRate, doc.LocationId, null);

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
                doc.CurrencyCode, payout.Amount, doc.Reference, "Till Refund",
                doc.ExchangeRate, doc.LocationId, null);

        var totalRowCount = 0;
        var merged = new List<MoneyRow>();

        foreach (var query in new[] { payments, tenders, change, payouts })
        {
            totalRowCount += await query.CountAsync(cancellationToken);
            merged.AddRange(await query.Take(maxRows).ToListAsync(cancellationToken));
        }

        var drawer = DrawerMovements(organizationId, range);
        totalRowCount += await drawer.CountAsync(cancellationToken);
        var drawerPage = await drawer.Take(maxRows).ToListAsync(cancellationToken);
        if (drawerPage.Count > 0)
        {
            // The drawer is the account of the location's Cash-kind mode (phase 61), so that mode names its rows.
            var cashModes = await (
                    from link in db.PosLocationPaymentModes
                    where link.OrganizationId == organizationId
                    join mode in db.PaymentModes on link.PaymentModeId equals mode.Id
                    where mode.Kind == PaymentModeKind.Cash && mode.AccountId != null
                    select new { link.BillingLocationId, mode.AccountId, mode.Name })
                .ToListAsync(cancellationToken);
            var cashModeOf = cashModes
                .GroupBy(x => (x.BillingLocationId, AccountId: x.AccountId!.Value))
                .ToDictionary(g => g.Key, g => g.Min(x => x.Name)!);

            merged.AddRange(drawerPage.Select(m => new MoneyRow(
                m.SessionCode, NepalTime.LocalDate(m.CreatedAt),
                m.Direction == PosCashMovementDirection.In ? PaymentDirection.Received : PaymentDirection.Paid,
                "Approved", null, cashModeOf.GetValueOrDefault((m.BillingLocationId, m.CashAccountId)),
                CurrencyCatalog.BaseCode, m.Amount, m.Note,
                m.Direction == PosCashMovementDirection.In ? "Till Cash In" : "Till Cash Out",
                ExchangeRates.BaseRate, m.BillingLocationId, m.AccountId)));
        }

        var locations = await ExportLookups.LocationsAsync(db, organizationId, cancellationToken);
        var accounts = await ExportLookups.AccountsAsync(db, organizationId, cancellationToken);

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
                p.ExchangeRate,
                locations.NameOf(p.LocationId),
                accounts.NameOf(p.AccountId),
            })
            .ToList();

        return new ExportCategoryResult(rows, totalRowCount);
    }

    /// <summary>Phase 78 -- a drawer's cash movements in the period, in the order they were recorded. The period is Nepal
    /// days, so each bound is a Nepal midnight, and each is its own <c>Where</c> rather than a null test inside one.</summary>
    private IQueryable<DrawerMovement> DrawerMovements(Guid organizationId, ExportDateRange range)
    {
        var movements =
            from movement in db.PosCashMovements
            join session in db.PosSessions on movement.PosSessionId equals session.Id
            where session.OrganizationId == organizationId
            select new { movement, session };

        if (range.From is { } from)
        {
            var fromInstant = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), NepalTime.Offset);
            movements = movements.Where(x => x.movement.CreatedAt >= fromInstant);
        }

        if (range.To is { } to)
        {
            var beforeInstant = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), NepalTime.Offset);
            movements = movements.Where(x => x.movement.CreatedAt < beforeInstant);
        }

        return movements
            .OrderBy(x => x.movement.CreatedAt)
            .Select(x => new DrawerMovement(
                x.session.Code, x.movement.CreatedAt, x.movement.Direction, x.movement.Amount, x.movement.Note,
                x.session.BillingLocationId, x.session.CashAccountId, x.movement.AccountId));
    }

    private sealed record DrawerMovement(
        string SessionCode,
        DateTimeOffset CreatedAt,
        PosCashMovementDirection Direction,
        decimal Amount,
        string? Note,
        Guid BillingLocationId,
        Guid CashAccountId,
        Guid AccountId);

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
        string Source,
        decimal ExchangeRate,
        Guid? LocationId,
        Guid? AccountId);
}
