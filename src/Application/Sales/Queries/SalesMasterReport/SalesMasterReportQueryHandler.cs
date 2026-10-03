using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesMasterReport;

public sealed class SalesMasterReportQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SalesMasterReportQuery, SalesMasterReportDto>
{
    public async Task<SalesMasterReportDto> Handle(SalesMasterReportQuery request, CancellationToken cancellationToken)
    {
        // Phase 32b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null unless the tenant has turned the toggle on
        // AND the caller's role carries location-specific grants, so no existing tenant changes.
        // This was the only consumer for three phases; phase 35b gave the setting the other 35
        // reports it was always meant to govern, closing phase-32b's carried item #1.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);

        var invoiceQuery = db.Invoices.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } invoiceContactId)
        {
            invoiceQuery = invoiceQuery.Where(x => x.ContactId == invoiceContactId);
        }

        if (request.WarehouseId is { } invoiceWarehouseId)
        {
            invoiceQuery = invoiceQuery.Where(x => x.WarehouseId == invoiceWarehouseId);
        }

        // Phase 32 -- the Billing Location filter. Applied to both document queries directly, because
        // unlike WarehouseId a Credit Note carries its own LocationId (it is in the default sales-only
        // scope), so there is no referrer lookup to fall back on. Phase 35b moved the two conditions
        // into ReportLocationFilter, which is now the one place 27 reports state them.
        invoiceQuery = invoiceQuery.AtLocations(request.LocationId, reportLocations);
        if (request.Channel is { } invoiceChannel)
        {
            invoiceQuery = invoiceQuery.Where(x => x.Channel == invoiceChannel);
        }

        var invoices = await invoiceQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.WarehouseId, x.LocationId, x.Code, x.Reference, x.Date,
                x.Channel, x.OrderType, x.PosSessionId,
            })
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(x => x.Id).ToList();

        var invoiceLinesQuery = db.InvoiceLines.Where(x => invoiceIds.Contains(x.InvoiceId));
        if (request.ProductId is { } invoiceProductId)
        {
            invoiceLinesQuery = invoiceLinesQuery.Where(x => x.ProductId == invoiceProductId);
        }

        var invoiceLines = await invoiceLinesQuery
            .Select(x => new { x.InvoiceId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.ServiceChargeAmount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNoteQuery = db.CreditNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } creditNoteContactId)
        {
            creditNoteQuery = creditNoteQuery.Where(x => x.ContactId == creditNoteContactId);
        }

        creditNoteQuery = creditNoteQuery.AtLocations(request.LocationId, reportLocations);
        if (request.Channel is { } creditNoteChannel)
        {
            creditNoteQuery = creditNoteQuery.Where(x => x.Channel == creditNoteChannel);
        }

        var creditNotes = await creditNoteQuery
            .Select(x => new
            {
                x.Id, x.ContactId, x.LocationId, x.Code, x.Reference, x.Date, x.ReferrerType, x.ReferrerId,
                x.Channel, x.PosSessionId,
            })
            .ToListAsync(cancellationToken);
        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();

        var creditNoteLinesQuery = db.CreditNoteLines.Where(x => creditNoteIds.Contains(x.CreditNoteId));
        if (request.ProductId is { } creditNoteProductId)
        {
            creditNoteLinesQuery = creditNoteLinesQuery.Where(x => x.ProductId == creditNoteProductId);
        }

        var creditNoteLines = await creditNoteLinesQuery
            .Select(x => new { x.CreditNoteId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.ServiceChargeAmount, x.VatAmount })
            .ToListAsync(cancellationToken);

        // CreditNote carries no WarehouseId of its own -- resolve it from the source Invoice when
        // this CreditNote actually reverses one, same lookup ApproveCreditNoteCommandHandler
        // already does for FIFO reversal. See SalesMasterReportQuery's doc comment.
        var referredInvoiceIds = creditNotes
            .Where(x => x.ReferrerType == DocumentType.Invoice && x.ReferrerId is not null)
            .Select(x => x.ReferrerId!.Value)
            .Distinct()
            .ToList();
        var referredInvoiceWarehouses = referredInvoiceIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Invoices
                .Where(x => x.OrganizationId == request.OrganizationId && referredInvoiceIds.Contains(x.Id))
                .Select(x => new { x.Id, x.WarehouseId })
                .ToDictionaryAsync(x => x.Id, x => x.WarehouseId, cancellationToken);

        var contactIds = invoices.Select(x => x.ContactId).Concat(creditNotes.Select(x => x.ContactId)).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.GroupId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var groupIds = contacts.Values.Where(x => x.GroupId is not null).Select(x => x.GroupId!.Value).Distinct().ToList();
        var groupNames = groupIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ContactGroups
                .Where(x => x.OrganizationId == request.OrganizationId && groupIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var warehouseIds = invoices.Select(x => x.WarehouseId).Concat(referredInvoiceWarehouses.Values).Distinct().ToList();
        var warehouseNames = warehouseIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Warehouses
                .Where(x => x.OrganizationId == request.OrganizationId && warehouseIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var locationIds = invoices.Select(x => x.LocationId)
            .Concat(creditNotes.Select(x => x.LocationId))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var locationNames = locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.BillingLocations
                .Where(x => x.OrganizationId == request.OrganizationId && locationIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var productIds = invoiceLines.Select(x => x.ProductId).Concat(creditNoteLines.Select(x => x.ProductId)).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var invoicesById = invoices.ToDictionary(x => x.Id);
        var creditNotesById = creditNotes.ToDictionary(x => x.Id);

        // Phase 66 -- the till's three columns, read only for till documents: the cashier is the
        // session's user, and the modes are the sale's tenders (a refund's payouts), joined to the
        // document *queries* rather than to a list of ids (phase 42).
        var tillColumns = await PosColumnsAsync(
            invoiceQuery.Where(x => x.Channel == SalesChannel.Pos),
            creditNoteQuery.Where(x => x.Channel == SalesChannel.Pos),
            [
                .. invoices.Where(x => x.PosSessionId != null).Select(x => x.PosSessionId!.Value)
                    .Concat(creditNotes.Where(x => x.PosSessionId != null).Select(x => x.PosSessionId!.Value))
                    .Distinct(),
            ],
            request.OrganizationId,
            cancellationToken);

        var rows = new List<SalesMasterReportRowDto>();

        foreach (var line in invoiceLines)
        {
            var invoice = invoicesById[line.InvoiceId];
            var contact = contacts[invoice.ContactId];
            var product = products[line.ProductId];

            var grossAmount = line.Quantity * line.Rate;
            var itemDiscount = grossAmount * line.DiscountPct / 100m;
            var netAfterLineDiscount = grossAmount - itemDiscount;
            var transactionDiscount = netAfterLineDiscount - line.Amount;

            rows.Add(new SalesMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.Invoice,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                invoice.WarehouseId, warehouseNames.GetValueOrDefault(invoice.WarehouseId),
                invoice.LocationId, invoice.LocationId is { } iLoc ? locationNames.GetValueOrDefault(iLoc) : null,
                invoice.Code, invoice.Reference, invoice.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, netAfterLineDiscount, itemDiscount, transactionDiscount, line.Amount,
                line.ServiceChargeAmount, line.VatRate, line.VatAmount,
                line.Amount + line.ServiceChargeAmount + line.VatAmount,
                invoice.Channel == SalesChannel.Pos ? invoice.OrderType : null,
                invoice.PosSessionId is { } saleSession ? tillColumns.Cashiers.GetValueOrDefault(saleSession) : null,
                tillColumns.SaleModes.GetValueOrDefault(invoice.Id)));
        }

        foreach (var line in creditNoteLines)
        {
            var creditNote = creditNotesById[line.CreditNoteId];

            Guid? resolvedWarehouseId = null;
            if (creditNote.ReferrerType == DocumentType.Invoice && creditNote.ReferrerId is { } sourceInvoiceId
                && referredInvoiceWarehouses.TryGetValue(sourceInvoiceId, out var sourceWarehouseId))
            {
                resolvedWarehouseId = sourceWarehouseId;
            }

            if (request.WarehouseId is { } filterWarehouseId && resolvedWarehouseId != filterWarehouseId)
            {
                continue;
            }

            var contact = contacts[creditNote.ContactId];
            var product = products[line.ProductId];

            var grossAmount = line.Quantity * line.Rate;
            var itemDiscount = grossAmount * line.DiscountPct / 100m;
            var netAfterLineDiscount = grossAmount - itemDiscount;
            var transactionDiscount = netAfterLineDiscount - line.Amount;

            rows.Add(new SalesMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.CreditNote,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                resolvedWarehouseId, resolvedWarehouseId is { } wId ? warehouseNames.GetValueOrDefault(wId) : null,
                creditNote.LocationId, creditNote.LocationId is { } cLoc ? locationNames.GetValueOrDefault(cLoc) : null,
                creditNote.Code, creditNote.Reference, creditNote.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, netAfterLineDiscount, itemDiscount, transactionDiscount, line.Amount,
                // Phase 63 -- a till refund line's service charge, in the column the sale's sits in.
                line.ServiceChargeAmount, line.VatRate, line.VatAmount,
                line.Amount + line.ServiceChargeAmount + line.VatAmount,
                null,
                creditNote.PosSessionId is { } refundSession ? tillColumns.Cashiers.GetValueOrDefault(refundSession) : null,
                tillColumns.RefundModes.GetValueOrDefault(creditNote.Id)));
        }

        var orderedRows = rows.OrderBy(x => x.EntryDate).ThenBy(x => x.EntryNo).ToList();
        var paged = request.ExportAll
            ? orderedRows.ToUnpagedResult()
            : orderedRows.ToPagedResult(request.Page, request.PageSize);
        var totalAmount = orderedRows.Sum(x => x.TotalAmount);

        return new SalesMasterReportDto(
            request.FromDate, request.ToDate, paged.Items, paged.Page, paged.PageSize, paged.TotalCount, totalAmount,
            orderedRows.Where(x => x.Type == DocumentType.Invoice).Sum(x => x.TotalAmount),
            orderedRows.Where(x => x.Type == DocumentType.CreditNote).Sum(x => x.TotalAmount));
    }

    private sealed record TillColumns(
        Dictionary<Guid, string> Cashiers, Dictionary<Guid, string> SaleModes, Dictionary<Guid, string> RefundModes);

    /// <summary>
    /// Phase 66 -- who rang each till document up and how it was settled. A sale's modes are its
    /// tenders' modes, plus "Credit" when the tenders (less change) settled less than the bill; a
    /// refund's are its payouts' modes, or "Credit" when it was all taken off the customer's account.
    /// </summary>
    private async Task<TillColumns> PosColumnsAsync(
        IQueryable<Invoice> tillSales,
        IQueryable<CreditNote> tillRefunds,
        List<Guid> sessionIds,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0)
        {
            return new TillColumns(new(), new(), new());
        }

        var cashiers = await (
                from session in db.PosSessions
                where session.OrganizationId == organizationId && sessionIds.Contains(session.Id)
                join user in db.Users on session.UserId equals user.Id
                select new { session.Id, user.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var modeNames = await db.PaymentModes
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var tenders = await (
                from tender in db.InvoiceTenders
                join invoice in tillSales on tender.InvoiceId equals invoice.Id
                select new { tender.InvoiceId, tender.PaymentModeId, tender.Amount })
            .ToListAsync(cancellationToken);
        var saleTotals = await (
                from line in db.InvoiceLines
                join invoice in tillSales on line.InvoiceId equals invoice.Id
                group line by line.InvoiceId into g
                select new { InvoiceId = g.Key, Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.VatAmount) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Total, cancellationToken);
        var saleHeaders = await tillSales
            .Select(x => new { x.Id, x.RoundOff, x.ChangeAmount })
            .ToListAsync(cancellationToken);

        var tendersBySale = tenders.ToLookup(x => x.InvoiceId);
        var saleModes = new Dictionary<Guid, string>();
        foreach (var sale in saleHeaders)
        {
            var paid = tendersBySale[sale.Id].ToList();
            var names = paid.Select(x => modeNames.GetValueOrDefault(x.PaymentModeId, "")).Distinct().ToList();
            var owed = saleTotals.GetValueOrDefault(sale.Id) + sale.RoundOff;
            if (owed - (paid.Sum(x => x.Amount) - sale.ChangeAmount) > 0m)
            {
                names.Add("Credit");
            }

            saleModes[sale.Id] = string.Join(", ", names);
        }

        var payouts = await (
                from payout in db.CreditNotePayouts
                join note in tillRefunds on payout.CreditNoteId equals note.Id
                select new { payout.CreditNoteId, payout.PaymentModeId })
            .ToListAsync(cancellationToken);
        var payoutsByRefund = payouts.ToLookup(x => x.CreditNoteId);
        var refundIds = await tillRefunds.Select(x => x.Id).ToListAsync(cancellationToken);
        var refundModes = refundIds.ToDictionary(
            id => id,
            id =>
            {
                var names = payoutsByRefund[id].Select(x => modeNames.GetValueOrDefault(x.PaymentModeId, "")).Distinct().ToList();
                return names.Count == 0 ? "Credit" : string.Join(", ", names);
            });

        return new TillColumns(cashiers, saleModes, refundModes);
    }
}
