using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Sales;

/// <summary>What finishing a till sale needs from the container: the posting rules and the policies.</summary>
internal sealed record PosSaleServices(
    IAppDbContext Db,
    IDocumentNumberGenerator NumberGenerator,
    Guid UserId,
    IGlPostingRule<InvoicePostingInput> PostingRule,
    IGlPostingRule<InvoiceTenderPostingInput> TenderPostingRule,
    IStockAvailabilityPolicy StockAvailabilityPolicy,
    IStockLedgerService StockLedgerService,
    ICreditLimitPolicy CreditLimitPolicy);

/// <param name="TendersField">The request field a refused settlement is a 400 on.</param>
/// <param name="SerialsByLine">Serial numbers per invoice line, for a serialised product.</param>
internal sealed record PosSaleCompletionInput(
    PosTillContext Till,
    PosSession Session,
    Contact Contact,
    Invoice Invoice,
    IReadOnlyCollection<Invoice.TenderInput> Tenders,
    decimal ChangeAmount,
    bool OverrideStockWarning,
    bool OverrideCreditLimitWarning,
    string TendersField,
    IReadOnlyDictionary<Guid, List<string>> SerialsByLine);

/// <summary>
/// Phase 61's sale engine from the moment a till sale's lines and round-off are on it: payment, the
/// abbreviated-invoice decision, the stock and credit checks, approval through the ERP's own path, and
/// the tenders as a second entry. Phase 65 lifted it out of <c>CreatePosSaleCommandHandler</c> rather
/// than copying it, because a bill for a restaurant order (<c>CreatePosOrderInvoiceCommand</c>) is the
/// same sale with different lines: a second door onto one engine, never a second engine. The caller
/// saves.
/// </summary>
internal static class PosSaleCompletion
{
    public static async Task CompleteAsync(PosSaleServices services, PosSaleCompletionInput input, CancellationToken cancellationToken)
    {
        var invoice = input.Invoice;

        try
        {
            invoice.Settle(input.Tenders, input.ChangeAmount);
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new ValidationFailure(input.TendersField, ex.Message)]);
        }

        await IssueAsAbbreviatedWhereAllowedAsync(services.Db, input, cancellationToken);

        // Stock first, then credit: the order the ERP's Approve checks them in, so a sale tripping
        // both shows them in the same order an invoice does.
        await EnsureStockAsync(services, input, cancellationToken);
        await EnsureCreditAsync(services, input, cancellationToken);

        // Touches the session's row, which carries a rowversion: a close committing between here and
        // the caller's save fails one of the two (409) rather than counting a drawer without this sale.
        input.Session.RecordActivity();

        services.Db.Invoices.Add(invoice);

        await InvoiceApprovalPosting.ApproveAndPostAsync(
            services.Db, services.NumberGenerator, services.PostingRule, services.StockLedgerService, services.UserId,
            invoice, input.SerialsByLine.ToDictionary(x => x.Key, x => x.Value), cancellationToken);

        await PostTendersAsync(services, invoice, input.Session, cancellationToken);
    }

    /// <summary>The named customer, or the tenant's walk-in when none is named (phase 60 Decision B).</summary>
    public static async Task<Contact> ResolveContactAsync(
        IAppDbContext db, Guid organizationId, Guid? contactId, CancellationToken cancellationToken)
    {
        if (contactId is { } id)
        {
            await SalesValidation.EnsureContactExistsAsync(db, organizationId, id, ContactType.Customer, cancellationToken);

            return await db.Contacts.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        }

        return await db.Contacts.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId && x.IsWalkInCustomer, cancellationToken)
            ?? throw new ConflictException(
                "This organization has no walk-in customer, so a sale must name its customer.");
    }

    /// <summary>The till's default warehouse, or the one the sale names (which must exist).</summary>
    public static async Task<Guid> ResolveWarehouseAsync(
        IAppDbContext db, Guid organizationId, PosTillContext till, Guid? requested, CancellationToken cancellationToken)
    {
        var warehouseId = requested ?? till.Location.WarehouseId
            ?? throw new ConflictException(
                $"'{till.Location.Name}' has no default warehouse, so the till does not know where stock leaves from. "
                + "Set one on the location, or name the warehouse on the sale.");
        await SalesValidation.EnsureWarehouseExistsAsync(db, organizationId, warehouseId, cancellationToken);
        return warehouseId;
    }

    /// <summary>
    /// Phase 62 Decision A -- an abbreviated tax invoice (VAT Rules 2053, Rule 18 as amended in 2076)
    /// is issued only when every condition the rule sets is met, and otherwise the full one:
    /// <list type="bullet">
    /// <item>the seller is VAT-registered -- only a registered person issues a tax invoice at all;</item>
    /// <item>the location records the Tax Officer's permission (Rule 18(1));</item>
    /// <item>the buyer is the walk-in. A customer who asks for a full tax invoice must be given one, and
    /// a buyer who wants to claim input VAT needs one; naming the customer at the till is how a cashier
    /// asks for it, so a named customer always gets the full invoice;</item>
    /// <item>the bill is at most <see cref="Invoice.AbbreviatedTaxInvoiceLimit"/> (Rule 18(6)).</item>
    /// </list>
    /// </summary>
    private static async Task IssueAsAbbreviatedWhereAllowedAsync(
        IAppDbContext db, PosSaleCompletionInput input, CancellationToken cancellationToken)
    {
        var invoice = input.Invoice;

        if (!input.Till.Settings.AbbreviatedTaxInvoiceEnabled || !input.Contact.IsWalkInCustomer
            || invoice.GrandTotal > Invoice.AbbreviatedTaxInvoiceLimit)
        {
            return;
        }

        var vatRegistered = await db.Organizations
            .Where(x => x.Id == invoice.OrganizationId)
            .Select(x => x.IsVatRegistered)
            .SingleAsync(cancellationToken);

        if (vatRegistered)
        {
            invoice.IssueAsAbbreviatedTaxInvoice();
        }
    }

    private static async Task EnsureStockAsync(
        PosSaleServices services, PosSaleCompletionInput input, CancellationToken cancellationToken)
    {
        var stockStatus = await services.StockAvailabilityPolicy.CheckAsync(input.Invoice, cancellationToken);

        if (stockStatus == StockAvailabilityStatus.Reject)
        {
            throw new ConflictException(
                "Not enough stock in the warehouse for this sale. Take the item off the bill or receive stock first.");
        }

        if (stockStatus == StockAvailabilityStatus.Warn && !input.OverrideStockWarning)
        {
            throw new StockAvailabilityWarningException(
                "One or more items on this sale exceed the stock in the warehouse. Ring it up again to continue anyway.");
        }
    }

    /// <summary>
    /// What is left unsettled is a receivable, and a receivable needs three things the cash part of a
    /// sale does not: a customer who can be chased for it (never the walk-in -- the vendor's defect 5),
    /// the right to approve one at this location (phase 61 Decision F), and room under the customer's
    /// credit limit -- checked on the <b>credit part only</b>, since the tendered part is settled on the
    /// spot and never reaches the customer's balance.
    /// </summary>
    private static async Task EnsureCreditAsync(
        PosSaleServices services, PosSaleCompletionInput input, CancellationToken cancellationToken)
    {
        var invoice = input.Invoice;
        var contact = input.Contact;

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
            services.Db, invoice.OrganizationId, services.UserId, PermissionKeys.InvoiceApprove, input.Till.Location.Id,
            cancellationToken);

        var creditStatus = await services.CreditLimitPolicy.CheckAsync(
            invoice.OrganizationId, contact.Id, invoice.CreditAmount, cancellationToken);

        if (creditStatus.Status == CreditLimitStatus.Reject)
        {
            throw new ConflictException(
                $"Leaving {invoice.CreditAmount:0.00} on credit would take {creditStatus.ContactName} "
                + $"({creditStatus.ContactCode}) to {creditStatus.ProjectedBalance:0.##}, past their credit limit of "
                + $"{creditStatus.CreditLimit:0.##}. Take more of the bill now, or raise the credit limit.");
        }

        if (creditStatus.Status == CreditLimitStatus.Warn && !input.OverrideCreditLimitWarning)
        {
            throw new CreditLimitWarningException(
                $"Leaving {invoice.CreditAmount:0.00} on credit takes {creditStatus.ContactName} "
                + $"({creditStatus.ContactCode}) to {creditStatus.ProjectedBalance:0.##}, past their credit limit of "
                + $"{creditStatus.CreditLimit:0.##}. Ring it up again to continue anyway.");
        }
    }

    /// <summary>The second entry (phase 59 Decision D). Nothing is posted when nothing was handed over.</summary>
    private static async Task PostTendersAsync(
        PosSaleServices services, Invoice invoice, PosSession session, CancellationToken cancellationToken)
    {
        if (invoice.Tenders.Count == 0)
        {
            return;
        }

        // InvoiceAccountResolver has already refused a tenant with no receivable account.
        var receivableAccountId = await services.Db.TenantSettings
            .Where(x => x.OrganizationId == invoice.OrganizationId)
            .Select(x => x.DefaultAccountsReceivableId)
            .SingleAsync(cancellationToken)
            ?? throw new ConflictException("Default Accounts Receivable account is not configured.");

        var lines = services.TenderPostingRule.BuildLines(new InvoiceTenderPostingInput(
            receivableAccountId,
            [.. invoice.Tenders.Select(x => new InvoiceTenderPostingLine(x.AccountId, x.Amount))],
            session.CashAccountId,
            invoice.ChangeAmount));

        if (lines.Count == 0)
        {
            return;
        }

        services.Db.GlJournalEntries.Add(GlJournalEntry.Post(
            invoice.OrganizationId, DocumentType.Invoice, invoice.Id, lines, invoice.LocationId));
    }
}
