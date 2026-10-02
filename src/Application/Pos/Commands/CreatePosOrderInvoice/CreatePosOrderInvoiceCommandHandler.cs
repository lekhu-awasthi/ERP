using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.Pos.Sales;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace ErpApp.Application.Pos.Commands.CreatePosOrderInvoice;

/// <summary>
/// Plans the part (the same planner the preview runs), builds the invoice from the order's lines,
/// hands it to the till's sale engine, and settles the order when nothing unbilled is left. One
/// <c>SaveChangesAsync</c> commits the invoice, both GL entries, the stock movements and the order.
///
/// <para><b>Two cashiers billing one order at once</b> must not both be counted against one remainder:
/// the order is touched, so its rowversion moves, and the second save is a concurrency 409. The
/// planner's read is the friendly half; the rowversion is the guarantee (phase 61's arrangement for a
/// session close against a sale).</para>
/// </summary>
public sealed class CreatePosOrderInvoiceCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<InvoicePostingInput> postingRule,
    IGlPostingRule<InvoiceTenderPostingInput> tenderPostingRule,
    IStockAvailabilityPolicy stockAvailabilityPolicy,
    IStockLedgerService stockLedgerService,
    ICreditLimitPolicy creditLimitPolicy)
    : IRequestHandler<CreatePosOrderInvoiceCommand, CreatePosOrderInvoiceResult>
{
    public async Task<CreatePosOrderInvoiceResult> Handle(
        CreatePosOrderInvoiceCommand request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        if (request.LocationId != session.BillingLocationId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.LocationId), $"Session {session.Code} is at another location; a bill is paid where its drawer is.")]);
        }

        var order = await PosRestaurant.LoadOrderAsync(db, request.OrganizationId, request.OrderId, cancellationToken);

        if (order.BillingLocationId != session.BillingLocationId)
        {
            throw new ConflictException(
                $"Order {order.Code} is at another location. An order is billed at a till of the location that took it.");
        }

        var till = await PosRestaurant.LoadAsync(db, request.OrganizationId, order.BillingLocationId, cancellationToken);

        var (plan, billed) = await PosOrderBillPlanner.PlanAsync(
            db, request.OrganizationId, order, till, new PosOrderBillPart(request.Split, request.Items, request.Parts),
            cancellationToken);

        // The order's customer unless the cashier names one -- which is how a guest asks for a full tax
        // invoice in their name, or how a bill is left on an account (phase 62 Decision A).
        var contact = await PosSaleCompletion.ResolveContactAsync(
            db, request.OrganizationId, request.ContactId ?? order.ContactId, cancellationToken);
        var warehouseId = await PosSaleCompletion.ResolveWarehouseAsync(
            db, request.OrganizationId, till, requested: null, cancellationToken);

        var tenders = await PosTenderModes.ResolveAsync(
            db, request.OrganizationId, till, session,
            [.. request.Tenders.Select(x => (x.PaymentModeId, x.Amount))],
            nameof(request.Tenders), cancellationToken);

        var invoice = Invoice.CreatePosSale(
            request.OrganizationId, contact.Id, warehouseId, request.Date, till.Location.Id, session.Id, order.OrderType);
        invoice.BillPosOrder(order.Id);

        foreach (var line in plan.Lines)
        {
            invoice.AddPosOrderLine(
                line.Line.Id, line.Line.ProductId, line.Quantity, line.Line.Rate, line.Line.VatRate, line.Line.UnitId,
                line.Line.ConversionFactor, line.Line.ServiceChargeRate, line.Figures);
        }

        if (plan.RoundOff != 0m)
        {
            invoice.SetPosRoundOff(plan.RoundOff);
        }

        await PosSaleCompletion.CompleteAsync(
            new PosSaleServices(
                db, numberGenerator, currentUser.UserId, postingRule, tenderPostingRule, stockAvailabilityPolicy,
                stockLedgerService, creditLimitPolicy),
            new PosSaleCompletionInput(
                till, session, contact, invoice, tenders, request.ChangeAmount, request.OverrideStockWarning,
                request.OverrideCreditLimitWarning, nameof(request.Tenders), new Dictionary<Guid, List<string>>()),
            cancellationToken);

        var invoicedNow = new Dictionary<Guid, decimal>(billed.Invoiced);
        foreach (var line in plan.Lines)
        {
            invoicedNow[line.Line.Id] = invoicedNow.GetValueOrDefault(line.Line.Id) + line.Quantity;
        }

        order.Touch();
        order.SettleIfFullyBilled(invoicedNow, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync(cancellationToken);

        return new CreatePosOrderInvoiceResult(
            invoice.Id, invoice.Code, invoice.GrandTotal, invoice.ServiceChargeTotal, invoice.RoundOff,
            invoice.TenderedAmount, invoice.ChangeAmount, invoice.CreditAmount, invoice.IsAbbreviatedTaxInvoice,
            order.Status, plan.LeftAfter);
    }
}
