using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Workflow.Queries.TransactionList;

/// <summary>
/// Phase 26a -- the reference product's <b>Transaction list</b> report (Reports &gt; Accounting),
/// read live on 2026-09-02 and recorded in erp-module-scan.md's confirm-live section: filters
/// <i>Txn Type</i> and <i>Transaction Status</i>; columns Transaction Date, Txn type, Transaction
/// No, Reference No, Status, Amount, Created By, Approved By, Approved At, Created At, Description.
/// The live dashboard's Transactions feed deep-links into it with <c>transaction_type[]</c> and
/// <c>status[]</c> query params, which is why both filters are lists rather than single values.
///
/// <para><b>Union shape, not a new projection.</b> This is the third cross-type union in the
/// codebase, after TransactionApprovalQuery (Phase 12) and RecentTransactionsQuery (Phase 23), and
/// it copies their idiom exactly: one concrete <c>db.Xs.Where(...)</c> block per document type
/// rather than one generic helper parameterised by a <c>Func</c> selector, because a captured
/// delegate inside <c>.Where()</c> compiles and then fails to translate against SQL Server
/// (phase-9 bug #1). It also copies Phase 23's <b>two-pass</b> shape: candidates are collected,
/// ordered and paged first, and only the returned page pays for its line sums, contact names and
/// user names.</para>
///
/// <para><b>Every status, including Draft.</b> Unlike every register report (Approved only, FR-9.10)
/// and unlike the recent-activity feed, the live Transaction list shows Draft rows -- its Status
/// filter offers Draft and Approved as the two values a user picks between, which only makes sense
/// if unfiltered means both. Void and Converted are included on the same reasoning: this report is
/// the flat "everything that exists" register, and a document silently missing from it would be
/// indistinguishable from one that was never created.</para>
///
/// <para><b>Admin-only.</b> Reports.TransactionList.View follows the standing rule: a flat
/// per-transaction register across every document type in the tenant, carrying who created and who
/// approved each one, is exactly the "flat per-transaction register / identity exposure" case that
/// resolves to Admin. Note this is deliberately <i>not</i> the per-type blanket-key pattern
/// TransactionApprovalQuery and RecentTransactionsQuery use: those are working screens a Member
/// uses daily and so gate per type inside the handler; this is a report, and the report itself is
/// the thing a Member is not given.</para>
///
/// <para><b>No footer total, deliberately.</b> phase-16c's rule is that a footer total must be
/// computed server-side over the whole filtered set -- but the honest answer here is that there is
/// no total to compute: an Invoice's gross, a Journal Voucher's debit side and an Inventory
/// Adjustment's value are not the same unit of account, and adding them would produce a number
/// with no meaning that a reader would nonetheless believe. RecentTransactionsQuery made the same
/// call for the same reason.</para>
/// </summary>
public sealed record TransactionListQuery(
    Guid OrganizationId,
    IReadOnlyList<DocumentType>? DocumentTypes = null,
    IReadOnlyList<TransactionListStatus>? Statuses = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PagedResult<TransactionListRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TransactionListView;
}

/// <summary>
/// The union of every lifecycle state across the 13 ApprovableTransaction types. Each type's own
/// status enum is a subset of this: all 13 have Draft/Approved/Void, and only Quotation and
/// PurchaseOrder also have Converted (grep-confirmed against the enums themselves, not assumed).
/// A shared enum is what lets one Status filter span the whole union.
/// </summary>
public enum TransactionListStatus
{
    Draft,
    Approved,
    Void,
    Converted,
}

/// <summary>
/// One transaction, whatever its type.
///
/// <para><c>Code</c> is still "DRAFT" for every Draft row by construction -- document numbers are
/// assigned at Approve, not at Create.</para>
///
/// <para><c>Amount</c> is the document's own headline figure in its own terms: gross total (lines
/// plus VAT) for the eight line-bearing sales/purchase types, the Payment's own Amount, the debit
/// side for a Journal Voucher, the transferred total for a Cash Transfer, and the valued total for
/// an Inventory Adjustment. A Warehouse Transfer moves stock between warehouses without changing
/// its value and has no amount at all, so it reports 0 rather than a number invented for the
/// column's sake.</para>
///
/// <para><c>CreatedByUserId</c> is <b>derived from the audit trail</b>, not stored on the document:
/// no transactional aggregate in this codebase carries a creator (grep-confirmed -- only Deal,
/// WorkTask, AlertDefinition, Organization and SmsCreditLedgerEntry do). AuditBehavior writes a
/// "Create" row per document, so the earliest such row is the creator. A document created before
/// Phase 16d introduced that behavior has no Create row and reports null, which is the honest
/// answer; inventing one from ApprovedByUserId would not be.</para>
///
/// <para><c>Description</c> is the contact's name, with the document's own Notes appended when it
/// has any. Only Expense carries a Notes field anywhere in the Domain, so in practice this is the
/// contact name for the nine contact-bearing types and empty for the four that have no contact --
/// the same "do not fabricate a column's source" rule ContactStatementQuery applied when it dropped
/// the live Description column entirely.</para>
/// </summary>
public sealed record TransactionListRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    Guid DocumentId,
    string Code,
    string? Reference,
    TransactionListStatus Status,
    decimal Amount,
    Guid? CreatedByUserId,
    string? CreatedByName,
    Guid? ApprovedByUserId,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    string? Description,
    PaymentDirection? Direction);
