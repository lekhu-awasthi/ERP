using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Queries.SalesMasterReport;

public sealed class SalesMasterReportQueryHandler(IAppDbContext db)
    : IRequestHandler<SalesMasterReportQuery, SalesMasterReportDto>
{
    public async Task<SalesMasterReportDto> Handle(SalesMasterReportQuery request, CancellationToken cancellationToken)
    {
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

        var invoices = await invoiceQuery
            .Select(x => new { x.Id, x.ContactId, x.WarehouseId, x.Code, x.Reference, x.Date })
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(x => x.Id).ToList();

        var invoiceLinesQuery = db.InvoiceLines.Where(x => invoiceIds.Contains(x.InvoiceId));
        if (request.ProductId is { } invoiceProductId)
        {
            invoiceLinesQuery = invoiceLinesQuery.Where(x => x.ProductId == invoiceProductId);
        }

        var invoiceLines = await invoiceLinesQuery
            .Select(x => new { x.InvoiceId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var creditNoteQuery = db.CreditNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } creditNoteContactId)
        {
            creditNoteQuery = creditNoteQuery.Where(x => x.ContactId == creditNoteContactId);
        }

        var creditNotes = await creditNoteQuery
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Reference, x.Date, x.ReferrerType, x.ReferrerId })
            .ToListAsync(cancellationToken);
        var creditNoteIds = creditNotes.Select(x => x.Id).ToList();

        var creditNoteLinesQuery = db.CreditNoteLines.Where(x => creditNoteIds.Contains(x.CreditNoteId));
        if (request.ProductId is { } creditNoteProductId)
        {
            creditNoteLinesQuery = creditNoteLinesQuery.Where(x => x.ProductId == creditNoteProductId);
        }

        var creditNoteLines = await creditNoteLinesQuery
            .Select(x => new { x.CreditNoteId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.Amount, x.VatAmount })
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

        var productIds = invoiceLines.Select(x => x.ProductId).Concat(creditNoteLines.Select(x => x.ProductId)).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var invoicesById = invoices.ToDictionary(x => x.Id);
        var creditNotesById = creditNotes.ToDictionary(x => x.Id);

        var rows = new List<SalesMasterReportRowDto>();

        foreach (var line in invoiceLines)
        {
            var invoice = invoicesById[line.InvoiceId];
            var contact = contacts[invoice.ContactId];
            var product = products[line.ProductId];

            rows.Add(new SalesMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.Invoice,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                invoice.WarehouseId, warehouseNames.GetValueOrDefault(invoice.WarehouseId),
                invoice.Code, invoice.Reference, invoice.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, line.Amount, line.VatRate, line.VatAmount, line.Amount + line.VatAmount));
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

            rows.Add(new SalesMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.CreditNote,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                resolvedWarehouseId, resolvedWarehouseId is { } wId ? warehouseNames.GetValueOrDefault(wId) : null,
                creditNote.Code, creditNote.Reference, creditNote.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, line.Amount, line.VatRate, line.VatAmount, line.Amount + line.VatAmount));
        }

        var orderedRows = rows.OrderBy(x => x.EntryDate).ThenBy(x => x.EntryNo).ToList();

        return new SalesMasterReportDto(request.FromDate, request.ToDate, orderedRows);
    }
}
