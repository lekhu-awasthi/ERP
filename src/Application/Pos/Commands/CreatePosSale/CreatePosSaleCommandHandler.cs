using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Sales;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Commands.CreatePosSale;

/// <summary>
/// Builds the invoice in Draft exactly as the ERP would (batches resolved, units frozen, serial rows
/// written), adds the till's three things -- per-line service charge, round-off and tenders -- then
/// approves it through <see cref="InvoiceApprovalPosting"/>, the path the ERP's Approve takes, and
/// posts the tenders as a second entry. One <c>SaveChangesAsync</c> at the end commits all of it;
/// the only thing that can outlive a refusal is a document number, which the ERP's Approve already
/// spends the same way.
/// </summary>
public sealed class CreatePosSaleCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<InvoicePostingInput> postingRule,
    IGlPostingRule<InvoiceTenderPostingInput> tenderPostingRule,
    IStockAvailabilityPolicy stockAvailabilityPolicy,
    IStockLedgerService stockLedgerService,
    ICreditLimitPolicy creditLimitPolicy)
    : IRequestHandler<CreatePosSaleCommand, CreatePosSaleResult>
{
    public async Task<CreatePosSaleResult> Handle(CreatePosSaleCommand request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        if (request.LocationId != session.BillingLocationId)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.LocationId), $"Session {session.Code} is at another location; a sale is rung up where its drawer is.")]);
        }

        var till = await PosTill.LoadAsync(db, request.OrganizationId, session.BillingLocationId, cancellationToken);
        var orderType = ResolveOrderType(request, till);

        var contact = await PosSaleCompletion.ResolveContactAsync(
            db, request.OrganizationId, request.ContactId, cancellationToken);
        var warehouseId = await PosSaleCompletion.ResolveWarehouseAsync(
            db, request.OrganizationId, till, request.WarehouseId, cancellationToken);

        var products = await LoadProductsAsync(request, cancellationToken);
        var tenders = await ResolveTendersAsync(request, till, session, cancellationToken);

        var invoice = Invoice.CreatePosSale(
            request.OrganizationId, contact.Id, warehouseId, request.Date, till.Location.Id, session.Id, orderType,
            request.DiscountPct);

        await AddLinesAsync(request, till, products, invoice, cancellationToken);

        if (till.Settings.RoundOffEnabled)
        {
            invoice.ApplyRoundOff();
        }

        var serialsByLine = invoice.Lines
            .Select((line, i) => (line.Id, Serials: request.Lines[i].SerialNumbers))
            .Where(x => x.Serials is { Count: > 0 })
            .ToDictionary(x => x.Id, x => x.Serials!.ToList());

        // Phase 65 -- everything from payment to the tender entry is the engine's, shared with the bill
        // for a restaurant order (CreatePosOrderInvoiceCommand).
        await PosSaleCompletion.CompleteAsync(
            new PosSaleServices(
                db, numberGenerator, currentUser.UserId, postingRule, tenderPostingRule, stockAvailabilityPolicy,
                stockLedgerService, creditLimitPolicy),
            new PosSaleCompletionInput(
                till, session, contact, invoice, tenders, request.ChangeAmount, request.OverrideStockWarning,
                request.OverrideCreditLimitWarning, nameof(request.Tenders), serialsByLine),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new CreatePosSaleResult(
            invoice.Id, invoice.Code, invoice.GrandTotal, invoice.ServiceChargeTotal, invoice.RoundOff,
            invoice.TenderedAmount, invoice.ChangeAmount, invoice.CreditAmount, invoice.IsAbbreviatedTaxInvoice);
    }

    private static PosTab ResolveOrderType(CreatePosSaleCommand request, PosTillContext till)
    {
        var tabs = PosTabs.For(till.Location.PosMode);
        var orderType = request.OrderType ?? till.Settings.EffectiveDefaultTab(till.Location.PosMode) ?? tabs[0];

        if (!tabs.Contains(orderType))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.OrderType),
                $"'{orderType}' is not a tab of a {till.Location.PosMode} till. It has: {string.Join(", ", tabs)}.")]);
        }

        return orderType;
    }

    private async Task<Dictionary<Guid, (bool ServiceChargeApplicable, Domain.Catalog.VatRate VatRate, bool AvailableForSale, string Name)>>
        LoadProductsAsync(CreatePosSaleCommand request, CancellationToken cancellationToken)
    {
        var productIds = request.Lines.Select(x => x.ProductId).Distinct().ToList();

        // Phase 24: refuses a variant parent and anything not this organization's.
        await SalesValidation.EnsureProductsExistAsync(db, request.OrganizationId, productIds, cancellationToken);

        var products = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ServiceChargeApplicable, x.VatRate, x.AvailableForSale, x.Name })
            .ToDictionaryAsync(x => x.Id, x => (x.ServiceChargeApplicable, x.VatRate, x.AvailableForSale, x.Name), cancellationToken);

        // Phase 60 Decision E -- Available For Sale has existed since phase 3 and been read by
        // nothing. The till is what it is for: a product a tenant has marked unsellable is refused at
        // the counter, not merely hidden from the grid.
        var unsellable = products.Values.Where(x => !x.AvailableForSale).Select(x => $"'{x.Name}'").ToList();
        if (unsellable.Count > 0)
        {
            throw new ConflictException(
                $"{string.Join(", ", unsellable)} {(unsellable.Count == 1 ? "is" : "are")} not available for sale.");
        }

        return products;
    }

    /// <summary>Each tender's mode must be one this till offers, and a cash tender must go into this
    /// drawer; the kind and account are frozen onto the tender (<see cref="PosTenderModes"/>).</summary>
    private Task<List<Invoice.TenderInput>> ResolveTendersAsync(
        CreatePosSaleCommand request, PosTillContext till, PosSession session, CancellationToken cancellationToken) =>
        PosTenderModes.ResolveAsync(
            db, request.OrganizationId, till, session,
            [.. request.Tenders.Select(x => (x.PaymentModeId, x.Amount))],
            nameof(request.Tenders), cancellationToken);

    private async Task AddLinesAsync(
        CreatePosSaleCommand request,
        PosTillContext till,
        Dictionary<Guid, (bool ServiceChargeApplicable, Domain.Catalog.VatRate VatRate, bool AvailableForSale, string Name)> products,
        Invoice invoice,
        CancellationToken cancellationToken)
    {
        // Phase 51 -- an issue may name a batch and must name the serials of a serialised product;
        // resolved exactly as an ERP invoice's lines are, never minting anything (not a receipt).
        var batches = await DocumentLineAllocationWriter.ResolveBatchesAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineAllocationWriter.LineAllocationInput(
                x.ProductId, x.Quantity, x.BatchNo, null, null, x.SerialNumbers))],
            isReceipt: false, cancellationToken);

        // Phase 52 -- the unit's factor frozen onto the line, read from the catalogue here and never again.
        var units = await DocumentLineUnitResolver.ResolveAsync(
            db, request.OrganizationId,
            [.. request.Lines.Select(x => new DocumentLineUnitResolver.LineUnitInput(x.ProductId, x.UnitId))],
            cancellationToken);

        for (var i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            var product = products[line.ProductId];

            // The location's rule times the product's flag (phase 59 Decision G), and never on a Take Away
            // or Delivery (phase 64's live read), frozen on the line.
            var serviceChargeRate = PosServiceCharge.RateFor(
                till.Settings, product.ServiceChargeApplicable, ResolveOrderType(request, till));

            invoice.AddPosLine(
                line.ProductId, line.Quantity, line.Rate, line.VatRate ?? product.VatRate, line.DiscountPct,
                units[i].UnitId, units[i].ConversionFactor, batches[i], serviceChargeRate);
        }

        await DocumentLineAllocationWriter.ReplaceSerialsAsync(
            db, request.OrganizationId, DocumentLineParentType.InvoiceLine,
            [],
            [.. invoice.Lines.Select((l, i) => (l.Id, request.Lines[i].SerialNumbers))],
            cancellationToken);
    }
}
