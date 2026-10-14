using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Refunds;
using ErpApp.Application.Sales;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.GetPosRefundableSale;

public sealed class GetPosRefundableSaleQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetPosRefundableSaleQuery, PosRefundableSaleDto>
{
    public async Task<PosRefundableSaleDto> Handle(GetPosRefundableSaleQuery request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tenders)
            .SingleOrDefaultAsync(x => x.Id == request.InvoiceId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Sale not found.");

        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);

        // Phase 84 -- which keys apply depends on where the loaded sale was made.
        var isHere = PosRefundPlanner.IsHere(invoice, till);
        if (isHere)
        {
            await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceView, till.Location.Id,
                cancellationToken);
        }
        else
        {
            await EnsureMayRefundHereAsync(request.OrganizationId, till.Location.Id, cancellationToken);
        }

        // The refund's own rules (a till sale, approved), so the screen refuses exactly what the refund would.
        PosRefundPlanner.EnsureRefundable(invoice, till);
        var returnWarehouseId = PosRefundPlanner.WarehouseFor(invoice, till);

        var remainingByKey = await SalesValidation.GetInvoiceRemainingByLineAsync(
            db, request.OrganizationId, invoice, cancellationToken);

        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.PrimaryUnitId }, cancellationToken);

        var unitIds = invoice.Lines
            .Select(x => x.UnitId ?? products.GetValueOrDefault(x.ProductId)?.PrimaryUnitId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == request.OrganizationId && unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        // Phase 91 -- the units each line sold, and which no approved refund has brought back (CreditNoteSerials, the
        // rule the refund itself is checked by).
        var soldSerials = await LineStockAllocator.LoadSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.InvoiceLine, [.. invoice.Lines.Select(x => x.Id)],
            cancellationToken);
        HashSet<(Guid ProductId, string SerialNo)> returnedSerials = soldSerials.Count == 0
            ? []
            : await CreditNoteSerials.ReturnedAsync(db, request.OrganizationId, invoice.Id, null, cancellationToken);

        // What is left is counted per (product, rate, VAT, discount, unit) key -- phase 6's cap -- so two
        // sale lines sharing a key share what is left of it. It is handed out to them in bill order.
        var left = new Dictionary<SaleLineKey, decimal>(remainingByKey);
        var lines = new List<PosRefundableLineDto>();
        foreach (var line in invoice.Lines)
        {
            var key = SaleLineKey.Of(line);
            var remaining = Math.Max(0m, Math.Min(line.Quantity, left.GetValueOrDefault(key)));
            left[key] = left.GetValueOrDefault(key) - remaining;

            var product = products.GetValueOrDefault(line.ProductId);
            var unitId = line.UnitId ?? product?.PrimaryUnitId;
            var serials = soldSerials.GetValueOrDefault(line.Id) ?? [];
            lines.Add(new PosRefundableLineDto(
                line.Id,
                line.ProductId,
                product?.Name ?? "",
                unitId is { } id ? unitNames.GetValueOrDefault(id, "") : "",
                line.Quantity,
                remaining,
                line.Rate,
                line.DiscountPct,
                line.ServiceChargeRate,
                line.PackagingFeeAmount,
                line.VatRate,
                line.LineTotal,
                line.DiscountPerUnit,
                serials,
                [.. serials.Where(s => !returnedSerials.Contains((line.ProductId, s)))]));
        }

        var priorRefunds = await db.CreditNotes
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == request.OrganizationId && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId == invoice.Id && x.Status == CreditNoteStatus.Approved)
            .OrderBy(x => x.ApprovedAt)
            .ToListAsync(cancellationToken);

        var sessionCode = await db.PosSessions
            .Where(x => x.Id == invoice.PosSessionId && x.OrganizationId == request.OrganizationId)
            .Select(x => x.Code)
            .SingleOrDefaultAsync(cancellationToken);

        var contact = await db.Contacts
            .Where(x => x.Id == invoice.ContactId && x.OrganizationId == request.OrganizationId)
            .Select(x => new { x.Name, x.IsWalkInCustomer })
            .SingleAsync(cancellationToken);

        var owed = await PosRefundPlanner.OwedOnSaleAsync(
            db, request.OrganizationId, invoice, NepalTime.LocalDate(DateTimeOffset.UtcNow), cancellationToken);

        // At the till's location: that is where the refund is made, numbered and paid out.
        var canRefund = !isHere || await MayRefundHereAsync(request.OrganizationId, till.Location.Id, cancellationToken);

        var soldAtLocationName = isHere
            ? null
            : await db.BillingLocations
                .Where(x => x.Id == invoice.LocationId && x.OrganizationId == request.OrganizationId)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken) ?? "";

        var returnWarehouseName = await db.Warehouses
            .Where(x => x.Id == returnWarehouseId && x.OrganizationId == request.OrganizationId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);

        return new PosRefundableSaleDto(
            invoice.Id,
            invoice.Code,
            invoice.Date,
            invoice.ApprovedAt,
            invoice.LocationId,
            sessionCode,
            invoice.ContactId,
            contact.Name,
            contact.IsWalkInCustomer,
            invoice.GrandTotal,
            invoice.SettledAmount,
            owed,
            lines,
            [.. priorRefunds.Select(x => new PosPriorRefundDto(x.Id, x.Code, x.Date, x.GrandTotal))],
            canRefund,
            soldAtLocationName,
            returnWarehouseName);
    }

    private async Task<bool> MayRefundHereAsync(Guid organizationId, Guid locationId, CancellationToken cancellationToken) =>
        await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, organizationId, currentUser.UserId, PermissionKeys.CreditNoteCreate, locationId, cancellationToken)
        && await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, organizationId, currentUser.UserId, PermissionKeys.CreditNoteApprove, locationId, cancellationToken);

    /// <summary>The same two keys, refused with the missing key's own 403.</summary>
    private async Task EnsureMayRefundHereAsync(Guid organizationId, Guid locationId, CancellationToken cancellationToken)
    {
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, organizationId, currentUser.UserId, PermissionKeys.CreditNoteCreate, locationId, cancellationToken);
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, organizationId, currentUser.UserId, PermissionKeys.CreditNoteApprove, locationId, cancellationToken);
    }
}
