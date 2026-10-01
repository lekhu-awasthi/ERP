using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.PrintPosReceipt;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.PrintPosRefundReceipt;

public sealed class PrintPosRefundReceiptCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PrintPosRefundReceiptCommand, PosRefundReceiptDto>
{
    public async Task<PosRefundReceiptDto> Handle(PrintPosRefundReceiptCommand request, CancellationToken cancellationToken)
    {
        var note = await db.CreditNotes
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Payouts)
            .SingleOrDefaultAsync(x => x.Id == request.CreditNoteId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        // The Domain refuses both as well; asked here first so each is a 409 naming the note.
        if (note.Channel != SalesChannel.Pos)
        {
            throw new ConflictException(
                $"Credit note {note.Code} was not made at a till, so it has no receipt. Print it from the credit note page.");
        }

        if (note.Status != CreditNoteStatus.Approved)
        {
            throw new ConflictException($"Credit note {note.Code} has been voided, so it is no longer a note to hand anyone.");
        }

        var printsSoFar = await db.CreditNotePrints.CountAsync(
            x => x.OrganizationId == request.OrganizationId && x.CreditNoteId == note.Id, cancellationToken);

        var print = CreditNotePrint.Record(note, printsSoFar, currentUser.UserId, DateTimeOffset.UtcNow);
        db.CreditNotePrints.Add(print);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique (organization, credit note, number) index -- phase 62 Decision B's race.
            throw new ConflictException(
                $"Credit note {note.Code} was printed somewhere else at the same moment. Print it again.");
        }

        return await BuildAsync(note, print, cancellationToken);
    }

    private async Task<PosRefundReceiptDto> BuildAsync(CreditNote note, CreditNotePrint print, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .Where(x => x.Id == note.OrganizationId)
            .Select(x => new { x.Name, x.Address, x.PanNumber, x.IsVatRegistered })
            .SingleAsync(cancellationToken);

        var location = await db.BillingLocations
            .Where(x => x.Id == note.LocationId && x.OrganizationId == note.OrganizationId)
            .Select(x => new { x.Name, x.Address })
            .SingleOrDefaultAsync(cancellationToken);

        var session = await db.PosSessions
            .Where(x => x.Id == note.PosSessionId && x.OrganizationId == note.OrganizationId)
            .Select(x => new { x.Code, x.UserId })
            .SingleOrDefaultAsync(cancellationToken);

        var invoice = note.ReferrerType == DocumentType.Invoice && note.ReferrerId is { } invoiceId
            ? await db.Invoices
                .Where(x => x.Id == invoiceId && x.OrganizationId == note.OrganizationId)
                .Select(x => new { x.Code, x.Date })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var userIds = new[] { print.PrintedByUserId, session?.UserId ?? Guid.Empty };
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var contact = await db.Contacts
            .Where(x => x.Id == note.ContactId && x.OrganizationId == note.OrganizationId)
            .Select(x => new { x.Name, x.Address, x.Pan, x.IsWalkInCustomer })
            .SingleAsync(cancellationToken);

        var productIds = note.Lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == note.OrganizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.PrimaryUnitId }, cancellationToken);

        var unitIds = note.Lines
            .Select(x => x.UnitId ?? products.GetValueOrDefault(x.ProductId)?.PrimaryUnitId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == note.OrganizationId && unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        var modeIds = note.Payouts.Select(x => x.PaymentModeId).Distinct().ToList();
        var modeNames = await db.PaymentModes
            .Where(x => x.OrganizationId == note.OrganizationId && modeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var lines = note.Lines
            .Select(x =>
            {
                var product = products.GetValueOrDefault(x.ProductId);
                var unitId = x.UnitId ?? product?.PrimaryUnitId;
                return new PosReceiptLineDto(
                    product?.Name ?? "",
                    x.Quantity,
                    unitId is { } id ? unitNames.GetValueOrDefault(id, "") : "",
                    x.Rate,
                    x.DiscountPct,
                    x.Amount,
                    x.ServiceChargeAmount,
                    x.VatRate,
                    x.VatAmount,
                    x.LineTotal);
            })
            .ToList();

        // Gross per line at the paisa, as on the sale's receipt, so Gross - Discount = Sub Total holds.
        var gross = note.Lines.Sum(x => decimal.Round(x.Quantity * x.Rate, Invoice.PosMoneyScale, MidpointRounding.AwayFromZero));
        var subTotal = note.Lines.Sum(x => x.Amount);
        var taxable = note.Lines.Where(x => x.VatRate == VatRate.ThirteenPercentVat).Sum(x => x.TaxableAmount);
        var nonTaxable = note.Lines.Where(x => x.VatRate != VatRate.ThirteenPercentVat).Sum(x => x.TaxableAmount);

        return new PosRefundReceiptDto(
            note.Id,
            note.Code,
            print.PrintNumber,
            print.PrintedAt,
            userNames.GetValueOrDefault(print.PrintedByUserId, ""),
            organization.Name,
            location?.Address ?? organization.Address,
            organization.PanNumber,
            organization.IsVatRegistered,
            location?.Name ?? "",
            note.Date,
            note.ApprovedAt,
            session?.Code ?? "",
            session is null ? "" : userNames.GetValueOrDefault(session.UserId, ""),
            invoice?.Code,
            invoice?.Date,
            contact.Name,
            contact.Address,
            contact.Pan,
            contact.IsWalkInCustomer,
            note.Reason,
            lines,
            gross,
            gross - subTotal,
            subTotal,
            note.ServiceChargeTotal,
            taxable,
            nonTaxable,
            note.Lines.Sum(x => x.VatAmount),
            note.RoundOff,
            note.GrandTotal,
            AmountInWords.Rupees(note.GrandTotal),
            note.Payouts
                .Select(x => new PosReceiptTenderDto(modeNames.GetValueOrDefault(x.PaymentModeId, ""), x.Kind, x.Amount))
                .ToList(),
            note.PaidOutAmount,
            note.ToAccountAmount);
    }
}
