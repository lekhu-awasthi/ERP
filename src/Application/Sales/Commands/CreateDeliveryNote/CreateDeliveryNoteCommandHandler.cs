using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.CreateDeliveryNote;

public sealed class CreateDeliveryNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateDeliveryNoteCommand, CreateDeliveryNoteResult>
{
    public async Task<CreateDeliveryNoteResult> Handle(CreateDeliveryNoteCommand request, CancellationToken cancellationToken)
    {
        await SalesValidation.EnsureContactExistsAsync(
            db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        // Phase 6's rule applied to the one conversion into a Delivery Note.
        if (request.ReferrerType == DocumentType.SalesOrder && request.ReferrerId is { } salesOrderId)
        {
            var salesOrder = await db.SalesOrders.SingleOrDefaultAsync(
                x => x.Id == salesOrderId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Sales order not found.");

            if (salesOrder.Status != SalesOrderStatus.Approved)
            {
                throw new ConflictException("Only an Approved sales order can be delivered.");
            }

            if (salesOrder.IsDelivered)
            {
                throw new ConflictException("This sales order has already been delivered on a Delivery Note.");
            }

            if (salesOrder.ContactId != request.ContactId)
            {
                throw new ConflictException("A delivery note must be to the same customer as its sales order.");
            }

            salesOrder.MarkDelivered();
        }

        var deliveryNote = DeliveryNote.Create(
            request.OrganizationId, request.ContactId, request.WarehouseId, request.Date, request.ExpectedDeliveryDate,
            request.Reference, request.TrackingNo, request.ShippingAddress,
            request.ReferrerType, request.ReferrerId, request.DiscountPct);

        deliveryNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        deliveryNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.DeliveryNote, request.LocationId, cancellationToken));
        deliveryNote.SetTerms(request.Terms);

        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            deliveryNote.AddLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor);
        }

        db.DeliveryNotes.Add(deliveryNote);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDeliveryNoteResult(deliveryNote.Id, deliveryNote.Code, deliveryNote.Status);
    }
}
