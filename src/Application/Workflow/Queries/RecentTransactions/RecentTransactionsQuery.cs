using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.RecentTransactions;

/// <summary>
/// The Home dashboard's unified recent-activity feed (`erp-module-scan.md`'s Home Tab, Phase 23
/// item 4), live-confirmed in this phase's Step 2: a single stream of recent transactions with
/// <b>All / Sales / Purchase / Payment / Receipt</b> tab filters and the empty state "No
/// Transactions Yet".
///
/// <para><b>This is the one new Application-layer aggregation Phase 23 wrote</b>, and it was added
/// deliberately rather than by drift. Decision F's rule for the dashboard was that every figure
/// comes from a query that already existed, and the feed was initially left out for exactly that
/// reason -- no existing query returns a mixed recent-transaction stream. The rule was then
/// overridden on purpose: a feed is the module scan's own third section of the Home Tab, and the
/// alternative (composing it client-side from five separate paginated list endpoints) cannot sort
/// or page a merged stream correctly. See `docs/phase-23-status.md`'s Decision F.</para>
///
/// <para><b>The tab list is the scope.</b> Sales covers Invoice and CreditNote; Purchase covers
/// PurchaseBill, DebitNote and Expense; Payment and Receipt are the two Directions of the one
/// Payment aggregate. JournalVoucher, CashTransfer, WarehouseTransfer and InventoryAdjustment are
/// deliberately absent -- the live product offers no tab for them, and they are not what a user
/// means by "recent transactions" on a sales/purchase dashboard. Quotation, SalesOrder and
/// PurchaseOrder are likewise absent: nothing has happened financially until they convert.</para>
///
/// <para><b>Approved only.</b> Drafts belong to the Transaction Approval queue, which is its own
/// screen; Void documents have been reversed, so listing them as recent activity would misstate
/// what happened. This matches every register report's Approved-only rule (FR-9.10).</para>
///
/// <para><b>Permission model copies TransactionApprovalQuery's, one level down.</b> The single
/// PermissionKey here is a blanket Admin+Member key whose main job is that
/// AuthorizationBehavior -- the only mechanism in this codebase that verifies the acting user
/// actually belongs to OrganizationId -- runs at all. The real visibility gating happens inside the
/// handler, one concrete per-document-type block at a time, against that type's own <c>*.View</c>
/// grant. So a Member who may see Purchase Bills but not Invoices gets a feed of Purchase Bills
/// rather than a 403, and the feed can never become a side door onto a document type the user is
/// not permitted to read.</para>
///
/// <para>Note there is no total on this DTO, and that is not an oversight: a feed has no footer to
/// compute one for. That is what keeps it clear of phase-16c's bug #1, which is about a footer
/// total silently becoming a page subtotal.</para>
/// </summary>
public sealed record RecentTransactionsQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    RecentTransactionFilter Filter = RecentTransactionFilter.All,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<RecentTransactionRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.RecentTransactionView;
}

/// <summary>The live product's five tabs, in its own order. Payment and Receipt split the one
/// Payment aggregate by Direction, exactly as the Angular routing already does.</summary>
public enum RecentTransactionFilter
{
    All,
    Sales,
    Purchase,
    Payment,
    Receipt,
}

/// <summary>
/// One row of the feed. <c>Amount</c> is the document's gross total (lines plus VAT) for the five
/// line-bearing types and the Payment's own Amount for a Payment. <c>Direction</c> is non-null only
/// for Payment rows, and is what decides whether the row links to the Customer Payment or Supplier
/// Payment detail page -- one aggregate, two Angular routes, as elsewhere in this codebase.
/// </summary>
public sealed record RecentTransactionRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    Guid DocumentId,
    string DocumentCode,
    Guid? ContactId,
    string? ContactName,
    decimal Amount,
    PaymentDirection? Direction);
