using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Refunds;
using ErpApp.Application.Sales;
using ErpApp.Domain.Common;
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

        if (invoice.Channel != SalesChannel.Pos)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was not rung up at a till. Return it with a credit note from the invoice page.");
        }

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException($"Invoice {invoice.Code} has been voided, so there is nothing to refund.");
        }

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

        // What is left is counted per (product, rate, VAT, discount, unit) key -- phase 6's cap -- so two
        // sale lines sharing a key share what is left of it. It is handed out to them in bill order.
        var left = new Dictionary<(Guid, decimal, Domain.Catalog.VatRate, decimal, Guid?), decimal>(remainingByKey);
        var lines = new List<PosRefundableLineDto>();
        foreach (var line in invoice.Lines)
        {
            var key = (line.ProductId, line.Rate, line.VatRate, line.DiscountPct, line.UnitId);
            var remaining = Math.Max(0m, Math.Min(line.Quantity, left.GetValueOrDefault(key)));
            left[key] = left.GetValueOrDefault(key) - remaining;

            var product = products.GetValueOrDefault(line.ProductId);
            var unitId = line.UnitId ?? product?.PrimaryUnitId;
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
                line.VatRate,
                line.LineTotal));
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

        var canRefund = invoice.LocationId is { } locationId
            && await GrantedPermissionReader.IsGrantedAtLocationAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteCreate, locationId, cancellationToken)
            && await GrantedPermissionReader.IsGrantedAtLocationAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteApprove, locationId, cancellationToken);

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
            canRefund);
    }
}
