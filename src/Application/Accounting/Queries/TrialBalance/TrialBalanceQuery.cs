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
/// </summary>
public sealed record TrialBalanceQuery(Guid OrganizationId, DateOnly AsOfDate)
    : IRequest<TrialBalanceDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.TrialBalanceView;
}

public sealed record TrialBalanceRowDto(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit);

/// <summary>TotalDebit/TotalCredit always match by construction (every posted GlJournalEntry is
/// itself balanced, per GlJournalEntry.Post's own invariant) -- IsBalanced is surfaced anyway,
/// same spirit as JournalVoucher's live "Difference: Rs. 0" check, so the UI/tests don't have to
/// re-derive it from the row list.</summary>
public sealed record TrialBalanceDto(DateOnly AsOfDate, IReadOnlyList<TrialBalanceRowDto> Rows, decimal TotalDebit, decimal TotalCredit)
{
    public bool IsBalanced => TotalDebit == TotalCredit;
}
