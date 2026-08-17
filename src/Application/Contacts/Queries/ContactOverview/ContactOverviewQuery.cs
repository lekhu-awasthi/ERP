using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Application.Contacts.Queries.ContactOverview;

/// <summary>
/// architecture-spec.md §4.2's ContactOverviewQuery -- the Contact detail page's Overview tab
/// (erp-module-scan.md line 91: "Overview (Opening Balance, DR/CR, Closing Balance, Recent
/// Transactions, 'View Full Statement')"), the follow-up Phase 9 deliberately deferred (see
/// phase-9-status.md's scope decision #13) rather than scope-creeping that phase beyond its four
/// named report screens.
///
/// A thin read over the same ContactLedgerReader Phase 9's ContactStatementQuery already uses -- not
/// a new computation. Closing Balance is literally "Opening Balance + every event up to today", the
/// exact formula ContactStatementQueryHandler already computes for its own running balance; Recent
/// Transactions is the same event list, capped and reverse-sorted. This inherits Statement's own
/// standalone-reversal-included behavior (an unlinked CreditNote/DebitNote counts here, unlike
/// Ageing) -- not re-derived differently, see phase-9-status.md's scope decision #9.
///
/// No FromDate/ToDate -- unlike Statement, the live Overview tab has no date-range picker at all,
/// just a live "as of now" snapshot, so AsOfDate is computed server-side as today
/// (DateOnly.FromDateTime(DateTime.UtcNow)), the same "as of now" pattern every
/// GetXConversionTemplateQuery handler already uses for its own default Date.
///
/// Takes ContactId alone, not a ContactType -- unlike Statement/Ageing's route-hardcoded
/// ContactType (Phase 9's "keep a bad/Lead value impossible" choice), this query looks the Contact's
/// own Type up itself from the single Contact detail page that already handles all three Types
/// (Customer/Supplier/Lead) on one route. A Lead naturally produces an empty event list (no document
/// type in this codebase can ever target a Lead-typed Contact -- enforced by
/// SalesValidation/PurchasingValidation, not merely assumed here), so no Lead special-case branch is
/// needed in the handler; only the DR/CR polarity choice needs one explicit fallback (Customer-style,
/// arbitrary but harmless since no real subledger exists for a Lead) -- see the handler.
///
/// PermissionKey reuses Contacts.Contact.View (Phase 3, granted to both Admin and Member) rather than
/// a new Reports.*.View key -- an explicit re-derivation against Phase 9's Admin-only Statement/Ageing
/// keys, not a silent default to either precedent. Phase 9's Admin-only case rested on two factors:
/// (1) a full unbounded per-transaction ledger for one Contact, and (2) a cross-Contact list exposing
/// every Contact's identity/PAN next to a balance. Overview has neither -- it's bounded to a handful
/// of Recent Transactions, for the exact one Contact a Member already has ContactView access to on
/// this same page (including that Contact's PAN, already shown in today's plain Overview form), not a
/// list of other Contacts. Gating a summary embedded in a page Member already sees daily behind a new
/// Admin-only key would block routine viewing for no matching exposure increase.
/// </summary>
public sealed record ContactOverviewQuery(Guid OrganizationId, Guid ContactId)
    : IRequest<ContactOverviewDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ContactView;
}

/// <summary>
/// No running Balance column (unlike ContactStatementRowDto) -- this is a bounded recent-activity
/// feed, not a ledger; the running balance per row is exactly what "View Full Statement" is for.
/// </summary>
public sealed record ContactOverviewTransactionDto(
    DateOnly Date, DocumentType DocumentType, string Code, string? Reference, decimal Debit, decimal Credit);

public sealed record ContactOverviewDto(
    Guid ContactId,
    string ContactCode,
    string ContactName,
    ContactType ContactType,
    decimal OpeningBalance,
    string OpeningBalanceType,
    decimal ClosingBalance,
    string ClosingBalanceType,
    IReadOnlyList<ContactOverviewTransactionDto> RecentTransactions);
