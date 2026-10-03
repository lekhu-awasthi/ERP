using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Queries.GetPosDayReport;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.PosPaymentSummary;

/// <summary>
/// Phase 66 -- how a payment is classified on the Payment Summary: the four payment-mode kinds, plus
/// <see cref="Credit"/> for what was left on (or taken off) a customer's account. The vendor's filter
/// offers exactly these, with Delivery for its delivery partners, which we do not have (§ 5).
/// </summary>
public enum PosPaymentType
{
    Cash,
    Card,
    EPayment,
    Other,
    Credit,
}

/// <summary>What a Payment Summary row records.</summary>
public enum PosPaymentEntry
{
    /// <summary>A sale's tender, as handed over.</summary>
    Tender,

    /// <summary>The change a cash sale handed back, out of its cash mode.</summary>
    Change,

    /// <summary>The part of a sale left on the customer's account.</summary>
    Credit,

    /// <summary>A refund's payout, as handed back.</summary>
    Payout,

    /// <summary>The part of a refund taken off the customer's account instead of paid out.</summary>
    CreditReturned,
}

/// <summary>
/// One movement of money at the till. <paramref name="Amount"/> is signed: what came in is positive,
/// what went back (change, payouts, credit taken back off an account) negative, so the rows sum to net
/// sales. <paramref name="PaymentModeId"/> is null on a credit row, whose account is the customer.
/// </summary>
public sealed record PosPaymentRowDto(
    DateOnly Date,
    DateTimeOffset At,
    DocumentType DocumentType,
    Guid DocumentId,
    string Code,
    Guid? LocationId,
    string? LocationName,
    string? Cashier,
    Guid ContactId,
    string ContactName,
    PosPaymentEntry Entry,
    PosPaymentType Type,
    Guid? PaymentModeId,
    string? PaymentModeName,
    string? AccountName,
    decimal Amount);

public sealed record PosPaymentTypeTotalDto(PosPaymentType Type, decimal Amount);

/// <summary>
/// <paramref name="Totals"/> and <paramref name="Total"/> are over every row the filters keep, not the
/// page (phase 16c). Unfiltered by type or mode, <paramref name="Total"/> is the period's net sales.
/// </summary>
public sealed record PosPaymentSummaryDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<PosPaymentRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PosPaymentTypeTotalDto> Totals,
    decimal Total);

/// <summary>
/// Phase 66 -- the Payment Summary: every way money moved at the till in a period -- each tender, the
/// change, the part left on credit, each payout and the part of a refund taken back off an account. The
/// vendor lists the cash tender net of change (317 for 500 handed over); this lists what was handed over
/// and the change apart, because the drawer saw both.
///
/// <para>Admin-only through <see cref="PermissionKeys.PosPaymentSummaryView"/>: a flat per-transaction
/// register naming the customer (Decision G). A <c>Reports.</c> key, so it takes the Billing Location
/// filter and the report location scope like every report (phase 35b).</para>
/// </summary>
public sealed record PosPaymentSummaryQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? LocationId = null,
    PosPaymentType? Type = null,
    Guid? PaymentModeId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PosPaymentSummaryDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature,
        ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.PosPaymentSummaryView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class PosPaymentSummaryQueryValidator : AbstractValidator<PosPaymentSummaryQuery>
{
    public PosPaymentSummaryQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate)
            .GreaterThanOrEqualTo(x => x.FromDate)
            .WithMessage("ToDate must be on or after FromDate.");
        RuleFor(x => x.ToDate)
            .Must((request, to) => to.DayNumber - request.FromDate.DayNumber < PosReportPeriod.MaxDays)
            .WithMessage($"A POS report covers at most {PosReportPeriod.MaxDays} days.");
        RuleFor(x => x.Type).IsInEnum();
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}

public sealed class PosPaymentSummaryQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PosPaymentSummaryQuery, PosPaymentSummaryDto>
{
    public async Task<PosPaymentSummaryDto> Handle(PosPaymentSummaryQuery request, CancellationToken cancellationToken)
    {
        if (request.LocationId is { } locationId
            && !await db.BillingLocations.AnyAsync(
                x => x.Id == locationId && x.OrganizationId == request.OrganizationId, cancellationToken))
        {
            throw new NotFoundException("Billing location not found.");
        }

        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        // The reader's own selections (phase 61 K): approved till documents dated in the period.
        var sales = PosSalesReader.TillSales(db, request.OrganizationId)
            .Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate)
            .AtLocations(request.LocationId, reportLocations);
        var refunds = PosSalesReader.TillRefunds(db, request.OrganizationId)
            .Where(x => x.Date >= request.FromDate && x.Date <= request.ToDate)
            .AtLocations(request.LocationId, reportLocations);

        var saleHeaders = await sales
            .Select(x => new
            {
                x.Id, x.Date, At = x.ApprovedAt ?? x.CreatedAt, x.Code, x.LocationId, x.ContactId, x.PosSessionId,
                x.RoundOff, x.ChangeAmount,
            })
            .ToListAsync(cancellationToken);
        var saleTotals = await (
                from line in db.InvoiceLines
                join invoice in sales on line.InvoiceId equals invoice.Id
                group line by line.InvoiceId into g
                select new { Id = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, cancellationToken);
        var tenders = (await (
                from tender in db.InvoiceTenders
                join invoice in sales on tender.InvoiceId equals invoice.Id
                select new { tender.InvoiceId, tender.Id, tender.PaymentModeId, tender.Kind, tender.AccountId, tender.Amount })
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.InvoiceId);

        var refundHeaders = await refunds
            .Select(x => new
            {
                x.Id, x.Date, At = x.ApprovedAt ?? x.CreatedAt, x.Code, x.LocationId, x.ContactId, x.PosSessionId, x.RoundOff,
            })
            .ToListAsync(cancellationToken);
        var refundTotals = await (
                from line in db.CreditNoteLines
                join note in refunds on line.CreditNoteId equals note.Id
                group line by line.CreditNoteId into g
                select new { Id = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.Id, x => x.Total, cancellationToken);
        var payouts = (await (
                from payout in db.CreditNotePayouts
                join note in refunds on payout.CreditNoteId equals note.Id
                select new { payout.CreditNoteId, payout.Id, payout.PaymentModeId, payout.Kind, payout.AccountId, payout.Amount })
            .ToListAsync(cancellationToken))
            .ToLookup(x => x.CreditNoteId);

        // Names for the tenant, not for a list of ids read back out of the period (phase 42).
        var modeNames = await db.PaymentModes.Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var accountNames = await db.Accounts.Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var locationNames = await db.BillingLocations.Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var contactNames = await db.Contacts.Where(x => x.OrganizationId == request.OrganizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var cashiers = await (
                from session in db.PosSessions
                where session.OrganizationId == request.OrganizationId
                join user in db.Users on session.UserId equals user.Id
                select new { session.Id, user.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var rows = new List<PosPaymentRowDto>();

        foreach (var sale in saleHeaders)
        {
            PosPaymentRowDto Row(PosPaymentEntry entry, PosPaymentType type, Guid? modeId, string? account, decimal amount) =>
                new(sale.Date, sale.At, DocumentType.Invoice, sale.Id, sale.Code, sale.LocationId,
                    sale.LocationId is { } l ? locationNames.GetValueOrDefault(l) : null,
                    sale.PosSessionId is { } s ? cashiers.GetValueOrDefault(s) : null,
                    sale.ContactId, contactNames.GetValueOrDefault(sale.ContactId, ""),
                    entry, type, modeId, modeId is { } m ? modeNames.GetValueOrDefault(m) : null, account, amount);

            var saleTenders = tenders[sale.Id].OrderBy(x => x.Id).ToList();
            foreach (var tender in saleTenders)
            {
                rows.Add(Row(PosPaymentEntry.Tender, TypeOf(tender.Kind), tender.PaymentModeId,
                    accountNames.GetValueOrDefault(tender.AccountId), tender.Amount));
            }

            // Change is cash handed back, so it leaves through the sale's cash mode (a sale may give no
            // more change than its cash tendered -- phase 61), out of that mode's account.
            if (sale.ChangeAmount != 0m)
            {
                var cash = saleTenders.First(x => x.Kind == PaymentModeKind.Cash);
                rows.Add(Row(PosPaymentEntry.Change, PosPaymentType.Cash, cash.PaymentModeId,
                    accountNames.GetValueOrDefault(cash.AccountId), -sale.ChangeAmount));
            }

            var credit = saleTotals.GetValueOrDefault(sale.Id) + sale.RoundOff
                - (saleTenders.Sum(x => x.Amount) - sale.ChangeAmount);
            if (credit != 0m)
            {
                rows.Add(Row(PosPaymentEntry.Credit, PosPaymentType.Credit, null,
                    contactNames.GetValueOrDefault(sale.ContactId), credit));
            }
        }

        foreach (var refund in refundHeaders)
        {
            PosPaymentRowDto Row(PosPaymentEntry entry, PosPaymentType type, Guid? modeId, string? account, decimal amount) =>
                new(refund.Date, refund.At, DocumentType.CreditNote, refund.Id, refund.Code, refund.LocationId,
                    refund.LocationId is { } l ? locationNames.GetValueOrDefault(l) : null,
                    refund.PosSessionId is { } s ? cashiers.GetValueOrDefault(s) : null,
                    refund.ContactId, contactNames.GetValueOrDefault(refund.ContactId, ""),
                    entry, type, modeId, modeId is { } m ? modeNames.GetValueOrDefault(m) : null, account, amount);

            var refundPayouts = payouts[refund.Id].OrderBy(x => x.Id).ToList();
            foreach (var payout in refundPayouts)
            {
                rows.Add(Row(PosPaymentEntry.Payout, TypeOf(payout.Kind), payout.PaymentModeId,
                    accountNames.GetValueOrDefault(payout.AccountId), -payout.Amount));
            }

            var toAccount = refundTotals.GetValueOrDefault(refund.Id) + refund.RoundOff - refundPayouts.Sum(x => x.Amount);
            if (toAccount != 0m)
            {
                rows.Add(Row(PosPaymentEntry.CreditReturned, PosPaymentType.Credit, null,
                    contactNames.GetValueOrDefault(refund.ContactId), -toAccount));
            }
        }

        IEnumerable<PosPaymentRowDto> kept = rows;
        if (request.Type is { } onlyType)
        {
            kept = kept.Where(x => x.Type == onlyType);
        }

        if (request.PaymentModeId is { } onlyMode)
        {
            kept = kept.Where(x => x.PaymentModeId == onlyMode);
        }

        var ordered = kept
            .OrderBy(x => x.Date)
            .ThenBy(x => x.At)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Entry)
            .ToList();

        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);

        return new PosPaymentSummaryDto(
            request.FromDate,
            request.ToDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            [.. ordered.GroupBy(x => x.Type).OrderBy(g => g.Key).Select(g => new PosPaymentTypeTotalDto(g.Key, g.Sum(x => x.Amount)))],
            ordered.Sum(x => x.Amount));
    }

    /// <summary>A payment mode's kind as the report's type, by name (never by ordinal: the two enums
    /// number their members differently, and <see cref="PosPaymentType.Credit"/> has no kind).</summary>
    public static PosPaymentType TypeOf(PaymentModeKind kind) =>
        Enum.Parse<PosPaymentType>(kind.ToString());
}
