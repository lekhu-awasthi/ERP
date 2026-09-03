using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.TrialBalance;

/// <summary>
/// Phase 8a's first of three "pure GL queries" (roadmap Phase 8+ section) -- every active Account's
/// net Debit/Credit balance from GlLine joined to GlJournalEntry, cut off at AsOfDate (end of day
/// UTC, see GlDateBoundary). Filters on GlJournalEntry.PostedAt (the Approve-time posting
/// timestamp), not any originating document's own business Date field -- see phase-8a-status.md's
/// scope-decision section for why that's an accepted approximation this phase, not silently baked
/// in.
///
/// <para><b>Compare (Phase 26a, FR-9.1).</b> When Compare is set the handler runs a second GL
/// aggregation at <see cref="Reports.ComparePeriod.PriorYearAsOf"/> -- the same calendar date one
/// year earlier -- and returns it as extra columns on this same response. Compare is off by
/// default so every existing caller keeps the exact response it had; when it is off, every
/// Compare* field is null rather than zero, so a screen can tell "not compared" from "compared and
/// the balance was nil".</para>
/// </summary>
public sealed record TrialBalanceQuery(Guid OrganizationId, DateOnly AsOfDate, bool Compare = false)
    : IRequest<TrialBalanceDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TrialBalanceView;
}

public sealed record TrialBalanceRowDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    decimal? CompareDebit = null,
    decimal? CompareCredit = null);

/// <summary>TotalDebit/TotalCredit always match by construction (every posted GlJournalEntry is
/// itself balanced, per GlJournalEntry.Post's own invariant) -- IsBalanced is surfaced anyway,
/// same spirit as JournalVoucher's live "Difference: Rs. 0" check, so the UI/tests don't have to
/// re-derive it from the row list. CompareAsOfDate is the date the Compare columns were actually
/// computed at, echoed so the screen and the .xlsx can label them with a real date.</summary>
public sealed record TrialBalanceDto(
    DateOnly AsOfDate,
    IReadOnlyList<TrialBalanceRowDto> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    DateOnly? CompareAsOfDate = null,
    decimal? CompareTotalDebit = null,
    decimal? CompareTotalCredit = null)
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}
