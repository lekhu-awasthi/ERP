using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.UpdateDeliveryNote;

public sealed class UpdateDeliveryNoteCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateDeliveryNoteCommand, UpdateDeliveryNoteResult>
{
    public async Task<UpdateDeliveryNoteResult> Handle(UpdateDeliveryNoteCommand request, CancellationToken cancellationToken)
    {
        var deliveryNote = await db.DeliveryNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Delivery note not found.");

        if (deliveryNote.Status != DeliveryNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft delivery note can be edited.");
        }

        await SalesValidation.EnsureContactExistsAsync(
            db, request.OrganizationId, request.ContactId, ContactType.Customer, cancellationToken);
        await SalesValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, request.WarehouseId, cancellationToken);
        await SalesValidation.EnsureProductsExistAsync(
            db, request.OrganizationId, request.Lines.Select(x => x.ProductId), cancellationToken);

        if (deliveryNote.ReferrerId is not null && request.ContactId != deliveryNote.ContactId)
        {
            throw new ConflictException(
                "A delivery note raised from a sales order must stay with that order's customer.");
        }

        var oldLines = deliveryNote.Lines.ToList();

        deliveryNote.UpdateHeader(
            request.ContactId, request.WarehouseId, request.Date, request.ExpectedDeliveryDate, request.Reference,
            request.TrackingNo, request.ShippingAddress, request.DiscountPct);
        deliveryNote.SetCurrency(request.CurrencyCode, request.ExchangeRate);
        deliveryNote.SetLocation(await LocationResolver.ResolveAsync(
            db, request.OrganizationId, DocumentType.DeliveryNote, request.LocationId, cancellationToken));
        deliveryNote.SetTerms(request.Terms);

        deliveryNote.ClearLines();
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

        db.DeliveryNoteLines.RemoveRange(oldLines);
        db.DeliveryNoteLines.AddRange(deliveryNote.Lines);

        await db.SaveChangesAsync(cancellationToken);

        return new UpdateDeliveryNoteResult(deliveryNote.Id, deliveryNote.Code, deliveryNote.Status);
    }
}
