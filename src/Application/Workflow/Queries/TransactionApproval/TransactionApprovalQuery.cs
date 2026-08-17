using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.TransactionApproval;

/// <summary>
/// architecture-spec.md §4.9's TransactionApprovalQuery / product-requirements.md FR-10.2: a
/// unified queue listing every Draft-status document, across all 13 ApprovableTransaction types
/// this codebase has (Quotation/SalesOrder/Invoice/CreditNote/PurchaseOrder/PurchaseBill/Expense/
/// DebitNote/JournalVoucher/CashTransfer/WarehouseTransfer/InventoryAdjustment/Payment -- confirmed
/// by grep against PermissionKeys.cs's 13 existing *.Approve keys, not assumed), filtered to the
/// document types the current user is actually permitted to approve. Read-only v1 (roadmap Phase
/// 8+ Workflow bullet): each row links into that document's own existing detail page, where the
/// existing Approve button already works -- no bulk-approve-from-the-list action this phase (a real
/// stretch goal, deliberately deferred, see phase-12-status.md's scope decision).
///
/// PermissionKey is PermissionKeys.TransactionApprovalView, a blanket Admin+Member key -- not
/// primarily an exposure-control choice, but because AuthorizationBehavior is the only mechanism in
/// this codebase that verifies the acting user actually belongs to OrganizationId (every other
/// IOrganizationScoped request also implements IRequirePermission for the same reason, confirmed by
/// grep). The *visibility* gating FR-10.2 actually asks for -- "every Draft-status document the
/// current user is permitted to approve" -- happens inside the handler itself, one concrete
/// per-document-type block at a time, not via this single key: each of the 13 blocks below only
/// runs if the acting user's Role holds that specific type's own *.Approve grant (the same
/// OrganizationMemberships/RolePermissions join AuthorizationBehavior performs, resolved once up
/// front as a granted-keys set). See phase-12-status.md's scope decision for the full reasoning.
///
/// Implementation deliberately avoids one generic Func&lt;TDocument,...&gt;-parameterized LINQ
/// helper across all 13 types -- CLAUDE.md's own known-gotchas list (and phase-9-status.md's bug #1)
/// document that a captured delegate inside .Where() doesn't translate against a real SQL Server
/// provider. Each block is its own concrete `db.Xs.Where(...)` query instead.
/// </summary>
public sealed record TransactionApprovalQuery(Guid OrganizationId)
    : IRequest<TransactionApprovalQueueDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TransactionApprovalView;
}

/// <summary>
/// One row per Draft document. ContactId/ContactName are null for the four document types with no
/// Contact at all (JournalVoucher/CashTransfer/WarehouseTransfer/InventoryAdjustment). Direction is
/// non-null only for Payment (Received routes to the Customer Payment detail page, Paid to the
/// Supplier Payment one -- both Angular routes exist for the same underlying Payment aggregate, see
/// phase-12-status.md's scope decision on routing). Code is still DraftCode ("DRAFT") for every row
/// here by definition (document numbers are assigned at Approve, not Create -- see CLAUDE.md's own
/// numbering convention note), so Date/CreatedAt are what the row is sorted and identified by, not
/// Code.
/// </summary>
public sealed record TransactionApprovalRowDto(
    DocumentType DocumentType,
    Guid DocumentId,
    string Code,
    DateOnly Date,
    DateTimeOffset CreatedAt,
    Guid? ContactId,
    string? ContactName,
    string? Reference,
    PaymentDirection? Direction);

public sealed record TransactionApprovalQueueDto(IReadOnlyList<TransactionApprovalRowDto> Rows);
