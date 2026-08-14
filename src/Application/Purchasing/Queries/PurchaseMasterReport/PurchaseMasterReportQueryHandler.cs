using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.PurchaseMasterReport;

public sealed class PurchaseMasterReportQueryHandler(IAppDbContext db)
    : IRequestHandler<PurchaseMasterReportQuery, PurchaseMasterReportDto>
{
    public async Task<PurchaseMasterReportDto> Handle(PurchaseMasterReportQuery request, CancellationToken cancellationToken)
    {
        var purchaseBillQuery = db.PurchaseBills.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } purchaseBillContactId)
        {
            purchaseBillQuery = purchaseBillQuery.Where(x => x.ContactId == purchaseBillContactId);
        }

        if (request.WarehouseId is { } purchaseBillWarehouseId)
        {
            purchaseBillQuery = purchaseBillQuery.Where(x => x.WarehouseId == purchaseBillWarehouseId);
        }

        var purchaseBills = await purchaseBillQuery
            .Select(x => new { x.Id, x.ContactId, x.WarehouseId, x.Code, x.Reference, x.Date })
            .ToListAsync(cancellationToken);
        var purchaseBillIds = purchaseBills.Select(x => x.Id).ToList();

        var purchaseBillLinesQuery = db.PurchaseBillLines.Where(x => purchaseBillIds.Contains(x.PurchaseBillId));
        if (request.ProductId is { } purchaseBillProductId)
        {
            purchaseBillLinesQuery = purchaseBillLinesQuery.Where(x => x.ProductId == purchaseBillProductId);
        }

        var purchaseBillLines = await purchaseBillLinesQuery
            .Select(x => new { x.PurchaseBillId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        var debitNoteQuery = db.DebitNotes.Where(x =>
            x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
            && x.Date >= request.FromDate && x.Date <= request.ToDate);
        if (request.ContactId is { } debitNoteContactId)
        {
            debitNoteQuery = debitNoteQuery.Where(x => x.ContactId == debitNoteContactId);
        }

        var debitNotes = await debitNoteQuery
            .Select(x => new { x.Id, x.ContactId, x.Code, x.Reference, x.Date, x.ReferrerType, x.ReferrerId })
            .ToListAsync(cancellationToken);
        var debitNoteIds = debitNotes.Select(x => x.Id).ToList();

        var debitNoteLinesQuery = db.DebitNoteLines.Where(x => debitNoteIds.Contains(x.DebitNoteId));
        if (request.ProductId is { } debitNoteProductId)
        {
            debitNoteLinesQuery = debitNoteLinesQuery.Where(x => x.ProductId == debitNoteProductId);
        }

        var debitNoteLines = await debitNoteLinesQuery
            .Select(x => new { x.DebitNoteId, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.Amount, x.VatAmount })
            .ToListAsync(cancellationToken);

        // DebitNote carries no WarehouseId of its own -- resolve it from the source PurchaseBill
        // when this DebitNote actually reverses one, same lookup ApproveDebitNoteCommandHandler
        // already does for FIFO reversal. See PurchaseMasterReportQuery's doc comment.
        var referredPurchaseBillIds = debitNotes
            .Where(x => x.ReferrerType == DocumentType.PurchaseBill && x.ReferrerId is not null)
            .Select(x => x.ReferrerId!.Value)
            .Distinct()
            .ToList();
        var referredPurchaseBillWarehouses = referredPurchaseBillIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == request.OrganizationId && referredPurchaseBillIds.Contains(x.Id))
                .Select(x => new { x.Id, x.WarehouseId })
                .ToDictionaryAsync(x => x.Id, x => x.WarehouseId, cancellationToken);

        var contactIds = purchaseBills.Select(x => x.ContactId).Concat(debitNotes.Select(x => x.ContactId)).Distinct().ToList();
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

        var warehouseIds = purchaseBills.Select(x => x.WarehouseId).Concat(referredPurchaseBillWarehouses.Values).Distinct().ToList();
        var warehouseNames = warehouseIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Warehouses
                .Where(x => x.OrganizationId == request.OrganizationId && warehouseIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var productIds = purchaseBillLines.Select(x => x.ProductId).Concat(debitNoteLines.Select(x => x.ProductId)).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var purchaseBillsById = purchaseBills.ToDictionary(x => x.Id);
        var debitNotesById = debitNotes.ToDictionary(x => x.Id);

        var rows = new List<PurchaseMasterReportRowDto>();

        foreach (var line in purchaseBillLines)
        {
            var purchaseBill = purchaseBillsById[line.PurchaseBillId];
            var contact = contacts[purchaseBill.ContactId];
            var product = products[line.ProductId];

            rows.Add(new PurchaseMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.PurchaseBill,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                purchaseBill.WarehouseId, warehouseNames.GetValueOrDefault(purchaseBill.WarehouseId),
                purchaseBill.Code, purchaseBill.Reference, purchaseBill.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, line.Amount, line.VatRate, line.VatAmount, line.Amount + line.VatAmount));
        }

        foreach (var line in debitNoteLines)
        {
            var debitNote = debitNotesById[line.DebitNoteId];

            Guid? resolvedWarehouseId = null;
            if (debitNote.ReferrerType == DocumentType.PurchaseBill && debitNote.ReferrerId is { } sourcePurchaseBillId
                && referredPurchaseBillWarehouses.TryGetValue(sourcePurchaseBillId, out var sourceWarehouseId))
            {
                resolvedWarehouseId = sourceWarehouseId;
            }

            if (request.WarehouseId is { } filterWarehouseId && resolvedWarehouseId != filterWarehouseId)
            {
                continue;
            }

            var contact = contacts[debitNote.ContactId];
            var product = products[line.ProductId];

            rows.Add(new PurchaseMasterReportRowDto(
                contact.Id, contact.Code, contact.Name, DocumentType.DebitNote,
                contact.GroupId, contact.GroupId is { } groupId ? groupNames.GetValueOrDefault(groupId) : null,
                resolvedWarehouseId, resolvedWarehouseId is { } wId ? warehouseNames.GetValueOrDefault(wId) : null,
                debitNote.Code, debitNote.Reference, debitNote.Date,
                product.Id, product.Code, product.Name,
                line.Quantity, line.Rate, line.Amount, line.VatRate, line.VatAmount, line.Amount + line.VatAmount));
        }

        var orderedRows = rows.OrderBy(x => x.EntryDate).ThenBy(x => x.EntryNo).ToList();

        return new PurchaseMasterReportDto(request.FromDate, request.ToDate, orderedRows);
    }
}
