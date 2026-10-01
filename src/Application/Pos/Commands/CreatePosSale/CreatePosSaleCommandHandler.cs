using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Documents;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
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

        var contact = await ResolveContactAsync(request, cancellationToken);
        var warehouseId = request.WarehouseId ?? till.Location.WarehouseId
            ?? throw new ConflictException(
                $"'{till.Location.Name}' has no default warehouse, so the till does not know where stock leaves from. "
                + "Set one on the location, or name the warehouse on the sale.");
        await SalesValidation.EnsureWarehouseExistsAsync(db, request.OrganizationId, warehouseId, cancellationToken);

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

        try
        {
            invoice.Settle(tenders, request.ChangeAmount);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Tenders), ex.Message)]);
        }

        // Stock first, then credit: the order the ERP's Approve checks them in, so a sale tripping
        // both shows them in the same order an invoice does.
        await EnsureStockAsync(request, invoice, cancellationToken);
        await EnsureCreditAsync(request, invoice, contact, till, cancellationToken);

        // Touches the session's row, which carries a rowversion: a close committing between here and
        // the save below fails one of the two (409) rather than counting a drawer without this sale.
        session.RecordActivity();

        db.Invoices.Add(invoice);

        var serialsByLine = invoice.Lines
            .Select((line, i) => (line.Id, Serials: request.Lines[i].SerialNumbers))
            .Where(x => x.Serials is { Count: > 0 })
            .ToDictionary(x => x.Id, x => x.Serials!.ToList());

        await InvoiceApprovalPosting.ApproveAndPostAsync(
            db, numberGenerator, postingRule, stockLedgerService, currentUser.UserId, invoice, serialsByLine,
            cancellationToken);

        await PostTendersAsync(invoice, session, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new CreatePosSaleResult(
            invoice.Id, invoice.Code, invoice.GrandTotal, invoice.ServiceChargeTotal, invoice.RoundOff,
            invoice.TenderedAmount, invoice.ChangeAmount, invoice.CreditAmount);
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

    /// <summary>The named customer, or the tenant's walk-in when none is named (phase 60 Decision B).</summary>
    private async Task<Contact> ResolveContactAsync(CreatePosSaleCommand request, CancellationToken cancellationToken)
    {
        if (request.ContactId is { } contactId)
        {
            await SalesValidation.EnsureContactExistsAsync(
                db, request.OrganizationId, contactId, ContactType.Customer, cancellationToken);

            return await db.Contacts.AsNoTracking().SingleAsync(x => x.Id == contactId, cancellationToken);
        }

        return await db.Contacts.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId && x.IsWalkInCustomer, cancellationToken)
            ?? throw new ConflictException(
                "This organization has no walk-in customer, so a sale must name its customer.");
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

    /// <summary>
    /// Each tender's mode must be one this till offers (phase 60: linked, active, with an account),
    /// and a cash tender must go into <i>this</i> drawer -- its mode's account must be the session's
    /// drawer account, fixed when the session opened. The kind and account are frozen onto the tender.
    /// </summary>
    private async Task<List<Invoice.TenderInput>> ResolveTendersAsync(
        CreatePosSaleCommand request, PosTillContext till, PosSession session, CancellationToken cancellationToken)
    {
        if (request.Tenders.Count == 0)
        {
            return [];
        }

        var modeIds = request.Tenders.Select(x => x.PaymentModeId).Distinct().ToList();

        var offered = await (
                from link in db.PosLocationPaymentModes
                join mode in db.PaymentModes on link.PaymentModeId equals mode.Id
                where link.OrganizationId == request.OrganizationId && link.BillingLocationId == till.Location.Id
                      && modeIds.Contains(mode.Id)
                select new { mode.Id, mode.Name, mode.Kind, mode.AccountId, mode.IsActive })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var refused = modeIds.Where(id => !offered.TryGetValue(id, out var m) || !m.IsActive || m.AccountId is null).ToList();
        if (refused.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Tenders),
                $"{refused.Count} payment mode(s) on this sale are not offered at '{till.Location.Name}', are inactive, "
                + "or name no payment account. A till takes the modes linked to it under Configurations > Point of Sale.")]);
        }

        var strayCash = offered.Values
            .Where(x => x.Kind == PaymentModeKind.Cash && x.AccountId != session.CashAccountId)
            .Select(x => $"'{x.Name}'")
            .ToList();
        if (strayCash.Count > 0)
        {
            throw new ConflictException(
                $"Cash taken in {string.Join(", ", strayCash)} would post to a different account from session "
                + $"{session.Code}'s drawer. Point every Cash mode this till offers at the drawer's account.");
        }

        return [.. request.Tenders.Select(x =>
        {
            var mode = offered[x.PaymentModeId];
            return new Invoice.TenderInput(mode.Id, mode.Kind, mode.AccountId!.Value, x.Amount);
        })];
    }

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

            // The location's rule times the product's flag (phase 59 Decision G), frozen on the line.
            var serviceChargeRate = till.Settings.ServiceChargeEnabled && product.ServiceChargeApplicable
                ? till.Settings.ServiceChargeRate
                : 0m;

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

    private async Task EnsureStockAsync(CreatePosSaleCommand request, Invoice invoice, CancellationToken cancellationToken)
    {
        var stockStatus = await stockAvailabilityPolicy.CheckAsync(invoice, cancellationToken);

        if (stockStatus == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException(
                "Not enough stock in the warehouse for this sale. Take the item off the bill or receive stock first.");
        }

        if (stockStatus == StockAvailabilityStatus.Warn && !request.OverrideStockWarning)
        {
            throw new StockAvailabilityWarningException(
                "One or more items on this sale exceed the stock in the warehouse. Ring it up again to continue anyway.");
        }
    }

    /// <summary>
    /// What is left unsettled is a receivable, and a receivable needs three things the cash part of a
    /// sale does not: a customer who can be chased for it (never the walk-in -- the vendor's defect 5),
    /// the right to approve one at this location (Decision F), and room under the customer's credit
    /// limit -- checked on the <b>credit part only</b>, since the tendered part is settled on the spot
    /// and never reaches the customer's balance.
    /// </summary>
    private async Task EnsureCreditAsync(
        CreatePosSaleCommand request, Invoice invoice, Contact contact, PosTillContext till,
        CancellationToken cancellationToken)
    {
        if (invoice.CreditAmount <= 0m)
        {
            return;
        }

        if (contact.IsWalkInCustomer)
        {
            throw new ConflictException(
                $"{invoice.CreditAmount:0.00} of this bill is unpaid, and the walk-in customer cannot be given credit. "
                + "Take the rest in another mode, or name the customer who owes it.");
        }

        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceApprove, till.Location.Id,
            cancellationToken);

        var creditStatus = await creditLimitPolicy.CheckAsync(
            request.OrganizationId, contact.Id, invoice.CreditAmount, cancellationToken);

        if (creditStatus.Status == CreditLimitStatus.Reject)
        {
            throw new ConflictException(
                $"Leaving {invoice.CreditAmount:0.00} on credit would take {creditStatus.ContactName} "
                + $"({creditStatus.ContactCode}) to {creditStatus.ProjectedBalance:0.##}, past their credit limit of "
                + $"{creditStatus.CreditLimit:0.##}. Take more of the bill now, or raise the credit limit.");
        }

        if (creditStatus.Status == CreditLimitStatus.Warn && !request.OverrideCreditLimitWarning)
        {
            throw new CreditLimitWarningException(
                $"Leaving {invoice.CreditAmount:0.00} on credit takes {creditStatus.ContactName} "
                + $"({creditStatus.ContactCode}) to {creditStatus.ProjectedBalance:0.##}, past their credit limit of "
                + $"{creditStatus.CreditLimit:0.##}. Ring it up again to continue anyway.");
        }
    }

    /// <summary>The second entry (phase 59 Decision D). Nothing is posted when nothing was handed over.</summary>
    private async Task PostTendersAsync(Invoice invoice, PosSession session, CancellationToken cancellationToken)
    {
        if (invoice.Tenders.Count == 0)
        {
            return;
        }

        // InvoiceAccountResolver has already refused a tenant with no receivable account.
        var receivableAccountId = await db.TenantSettings
            .Where(x => x.OrganizationId == invoice.OrganizationId)
            .Select(x => x.DefaultAccountsReceivableId)
            .SingleAsync(cancellationToken)
            ?? throw new ConflictException("Default Accounts Receivable account is not configured.");

        var lines = tenderPostingRule.BuildLines(new InvoiceTenderPostingInput(
            receivableAccountId,
            [.. invoice.Tenders.Select(x => new InvoiceTenderPostingLine(x.AccountId, x.Amount))],
            session.CashAccountId,
            invoice.ChangeAmount));

        if (lines.Count == 0)
        {
            return;
        }

        db.GlJournalEntries.Add(GlJournalEntry.Post(
            invoice.OrganizationId, DocumentType.Invoice, invoice.Id, lines, invoice.LocationId));
    }
}
