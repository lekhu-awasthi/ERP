using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ContactStatement;

/// <summary>
/// architecture-spec.md §4.2's ContactStatementQuery -- the running-balance ledger. One shared
/// handler answers both Customer and Supplier Statement (confirmed live shape for both -- Customer
/// per architecture-spec.md line 277; Supplier read live from the reference product's own Supplier
/// Statement screen during this phase's design pass: identical Txn Date/Txn Type/Txn No/Reference No/
/// Debit/Credit/Balance columns, an Opening Balance row and a Closing Balance row, balances suffixed
/// "DR"/"CR"), same ContactType-discriminates-the-underlying-document-types choice
/// ContactAgeingSummaryQuery makes -- see that query's doc comment and phase-9-status.md.
///
/// The spec's own "joins Accounting" phrasing doesn't translate literally: GlLine carries no
/// ContactId (only AccountId/Debit/Credit), and this codebase has no per-Contact GL subledger account
/// (AR/AP are single shared control accounts via TenantSettings' defaults, not one Account per
/// Contact) -- so a real per-Contact ledger has to be built from each document's own ContactId/Date/
/// Amount directly, the same document-register approach every Phase 8 report since Phase 8b already
/// established, not a GL join. See phase-9-status.md.
///
/// Single ContactId, not the live screen's multi-select -- every other single-party report in this
/// codebase (GetContactQuery, etc.) takes one id; the live UI's multi-select is a bulk-print
/// convenience layered over the same one-Account-at-a-time computation, not a different shape.
///
/// The live screen's "Description" column is omitted -- no document type in this codebase carries
/// genuine freetext narrative data to source it from (only Expense has a Notes field; populating it
/// for one document type and leaving the other five blank would be worse than omitting the column
/// entirely), the same "omit a column needing a capability that doesn't exist" precedent Annex 5 set.
/// </summary>
public sealed record ContactStatementQuery(
    Guid OrganizationId,
    ContactType ContactType,
    Guid ContactId,
    DateOnly FromDate,
    DateOnly ToDate,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<ContactStatementDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey =>
        ContactType == ContactType.Customer ? PermissionKeys.CustomerStatementView : PermissionKeys.SupplierStatementView;
}

/// <summary>
/// Debit/Credit follow real double-entry polarity, not a uniform "increases the balance -> Debit"
/// rule -- for a Customer (AR, debit-normal) a bill lands in Debit and a reduction in Credit; for a
/// Supplier (AP, credit-normal) it's the exact opposite, a bill lands in Credit and a reduction in
/// Debit. Confirmed directly against the live Supplier Statement screen: its Opening Balance row
/// carried its value in the Credit column, "-" in Debit, labeled "CR" -- a credit-normal balance
/// sitting in the credit column. Balance/OpeningBalance/ClosingBalance are always non-negative
/// magnitudes; the matching *Type ("DR"/"CR") string carries the sign so Angular never needs to know
/// which side is normal for which ContactType.
/// </summary>
public sealed record ContactStatementRowDto(
    DateOnly Date, DocumentType DocumentType, string Code, string? Reference,
    decimal Debit, decimal Credit, decimal Balance, string BalanceType);

public sealed record ContactStatementDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    ContactType ContactType,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal OpeningBalance,
    string OpeningBalanceType,
    IReadOnlyList<ContactStatementRowDto> Rows,
    decimal ClosingBalance,
    string ClosingBalanceType,
    int Page,
    int PageSize,
    int TotalCount);
