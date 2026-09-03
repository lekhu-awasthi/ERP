using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;

/// <summary>
/// Phase 26a -- the reference product's <b>GL Master Report</b> (Reports &gt; Accounting, URL slug
/// <c>general-ledger-materialized</c>), generated live on 2026-09-02. Filters: Period and Txn Type.
/// Columns, in the live order: Date, Txn Type, Txn No, Reference No, Account, SubAccount, Parent,
/// Group Type, Account Class, Debit, Credit. One row per GL line, denormalised -- the Sales Master
/// Report shape applied to the general ledger, exactly as the roadmap predicted.
///
/// <para><b>SubAccount is omitted, not blanked.</b> In the reference product a Contact <i>is</i> a
/// subledger account, so a posting can name both a control account and the contact under it; this
/// codebase has no per-Contact GL account at all -- AR/AP are single shared control accounts
/// resolved from TenantSettings, which <c>ContactStatementQuery</c> already records. The column was
/// empty on every row of the live report on this tenant anyway. Carrying a permanently-empty
/// column would imply a capability that does not exist; the same call Annex 5 and the Contact
/// Statement's own Description column set.</para>
///
/// <para><b>Date is the posting date, not the document date.</b> <c>GlJournalEntry</c> stamps
/// PostedAt at Approve time and stores no copy of the document's own business date, and every GL
/// report in this codebase since Phase 8a filters on PostedAt -- see phase-8a-status.md, where that
/// was recorded as an accepted approximation rather than silently baked in. This report shows the
/// same field it filters on, so a row can never appear outside the range printed above it. Moving
/// the whole GL report family onto document dates is a coherent future change; doing it for one
/// report would only make the family disagree with itself.</para>
///
/// <para><b>Admin-only</b> (Reports.GeneralLedgerMaster.View): a denormalised fact table over every
/// posted line in the tenant, which is the Sales/Purchase Master Report exposure -- and both of
/// those are Admin-only.</para>
///
/// <para><b>No footer total</b>, matching the live report, which has none: the sheet is already
/// balanced by construction (every entry it lists is), so a Debit/Credit grand total would restate
/// an invariant rather than tell the reader anything. It is also not computable from one page, and
/// phase-16c's rule is that a footer must cover the whole filtered set or not exist.</para>
/// </summary>
public sealed record GeneralLedgerMasterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    DocumentType? DocumentType = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PagedResult<GeneralLedgerMasterRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.GeneralLedgerMasterView;
}

/// <summary>
/// One posted GL line. <c>Direction</c> is non-null only for a Payment, and carries the same two
/// jobs it does everywhere else in this codebase: the live product renders the two Directions as
/// two different Txn Types ("Customer Payment" / "Supplier Payment"), and it decides which of the
/// two Angular detail routes the row links to.
/// </summary>
public sealed record GeneralLedgerMasterRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    Guid DocumentId,
    string? DocumentCode,
    string? Reference,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string ParentGroupName,
    string GroupTypeName,
    AccountRootType RootType,
    decimal Debit,
    decimal Credit,
    PaymentDirection? Direction);
