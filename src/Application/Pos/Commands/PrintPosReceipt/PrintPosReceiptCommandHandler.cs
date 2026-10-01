using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.PrintPosReceipt;

public sealed class PrintPosReceiptCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PrintPosReceiptCommand, PosReceiptDto>
{
    public async Task<PosReceiptDto> Handle(PrintPosReceiptCommand request, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tenders)
            .SingleOrDefaultAsync(x => x.Id == request.InvoiceId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        // The Domain refuses both as well; asked here first so each is a 409 naming the bill rather
        // than an InvalidOperationException's 500.
        if (invoice.Channel != SalesChannel.Pos)
        {
            throw new ConflictException(
                $"Invoice {invoice.Code} was not rung up at a till, so it has no receipt. Print it from the invoice page.");
        }

        if (invoice.Status != InvoiceStatus.Approved)
        {
            throw new ConflictException($"Invoice {invoice.Code} has been voided, so it is no longer a bill to hand anyone.");
        }

        var printsSoFar = await db.InvoicePrints.CountAsync(
            x => x.OrganizationId == request.OrganizationId && x.InvoiceId == invoice.Id, cancellationToken);

        var print = InvoicePrint.Record(invoice, printsSoFar, currentUser.UserId, DateTimeOffset.UtcNow);
        db.InvoicePrints.Add(print);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique (organization, invoice, number) index: someone else printed this bill in
            // the same instant and took this number. Renumbering silently would print a copy count
            // nobody can reconcile, so the cashier is asked to print again (Decision B).
            throw new ConflictException(
                $"Invoice {invoice.Code} was printed somewhere else at the same moment. Print it again.");
        }

        return await BuildAsync(invoice, print, cancellationToken);
    }

    private async Task<PosReceiptDto> BuildAsync(Invoice invoice, InvoicePrint print, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .Where(x => x.Id == invoice.OrganizationId)
            .Select(x => new { x.Name, x.Address, x.PanNumber, x.IsVatRegistered })
            .SingleAsync(cancellationToken);

        var location = await db.BillingLocations
            .Where(x => x.Id == invoice.LocationId && x.OrganizationId == invoice.OrganizationId)
            .Select(x => new { x.Name, x.Address })
            .SingleOrDefaultAsync(cancellationToken);

        var session = await db.PosSessions
            .Where(x => x.Id == invoice.PosSessionId && x.OrganizationId == invoice.OrganizationId)
            .Select(x => new { x.Code, x.UserId })
            .SingleOrDefaultAsync(cancellationToken);

        var userIds = new[] { print.PrintedByUserId, session?.UserId ?? Guid.Empty };
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var contact = await db.Contacts
            .Where(x => x.Id == invoice.ContactId && x.OrganizationId == invoice.OrganizationId)
            .Select(x => new { x.Name, x.Address, x.Pan, x.IsWalkInCustomer })
            .SingleAsync(cancellationToken);

        var productIds = invoice.Lines.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(x => x.OrganizationId == invoice.OrganizationId && productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.PrimaryUnitId }, cancellationToken);

        var unitIds = invoice.Lines
            .Select(x => x.UnitId ?? products.GetValueOrDefault(x.ProductId)?.PrimaryUnitId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var unitNames = await db.UnitsOfMeasurement
            .Where(x => x.OrganizationId == invoice.OrganizationId && unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, cancellationToken);

        var modeIds = invoice.Tenders.Select(x => x.PaymentModeId).Distinct().ToList();
        var modeNames = await db.PaymentModes
            .Where(x => x.OrganizationId == invoice.OrganizationId && modeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var lines = invoice.Lines
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

        // Gross is per line at the paisa, as the till rounds every line figure (phase 61 Decision L),
        // so Gross − Discount = Sub Total holds on the paper to the paisa.
        var gross = invoice.Lines.Sum(x => decimal.Round(x.Quantity * x.Rate, Invoice.PosMoneyScale, MidpointRounding.AwayFromZero));
        var subTotal = invoice.Lines.Sum(x => x.Amount);
        var taxable = invoice.Lines.Where(x => x.VatRate == VatRate.ThirteenPercentVat).Sum(x => x.TaxableAmount);
        var nonTaxable = invoice.Lines.Where(x => x.VatRate != VatRate.ThirteenPercentVat).Sum(x => x.TaxableAmount);

        var title = !organization.IsVatRegistered
            ? PosReceiptTitle.Invoice
            : invoice.IsAbbreviatedTaxInvoice ? PosReceiptTitle.AbbreviatedTaxInvoice : PosReceiptTitle.TaxInvoice;

        return new PosReceiptDto(
            invoice.Id,
            invoice.Code,
            title,
            print.PrintNumber,
            print.PrintedAt,
            userNames.GetValueOrDefault(print.PrintedByUserId, ""),
            organization.Name,
            location?.Address ?? organization.Address,
            organization.PanNumber,
            location?.Name ?? "",
            invoice.Date,
            invoice.ApprovedAt,
            session?.Code ?? "",
            session is null ? "" : userNames.GetValueOrDefault(session.UserId, ""),
            invoice.OrderType,
            contact.Name,
            contact.Address,
            contact.Pan,
            contact.IsWalkInCustomer,
            lines,
            gross,
            gross - subTotal,
            subTotal,
            invoice.ServiceChargeTotal,
            taxable,
            nonTaxable,
            invoice.Lines.Sum(x => x.VatAmount),
            invoice.RoundOff,
            invoice.GrandTotal,
            AmountInWords.Rupees(invoice.GrandTotal),
            invoice.Tenders
                .Select(x => new PosReceiptTenderDto(modeNames.GetValueOrDefault(x.PaymentModeId, ""), x.Kind, x.Amount))
                .ToList(),
            invoice.TenderedAmount,
            invoice.ChangeAmount,
            invoice.CreditAmount);
    }
}
