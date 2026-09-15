using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Crm.Queries.ListSmsLogs;

/// <summary>
/// One row per recipient (SmsLog's own granularity) -- a deliberate simplification over Tigg's own
/// Overview "Recent SMS" table (one row per send-batch): this codebase's SMS History always shows
/// the real per-recipient detail (who, what resolved text, when), letting a caller roll rows up by
/// BatchId client-side if a batch-level summary view is ever needed. ContactId is an optional
/// filter -- omitted for the standalone SMS module's own org-wide History tab, supplied for a
/// Contact's own "SMS History" activity sub-tab.
///
/// <para><b>Phase 45 (39 carried item #4) -- the search term.</b> Phase 39 exempted this query in
/// {@link SearchSweepGuardTests} as "a send history ordered newest-first", and named it as the one
/// of that phase's seven newly-visible lists whose re-entry condition would eventually be met: a
/// tenant sending enough SMS that the last page stops being what they want. It is met here by
/// construction rather than by observation -- a send to a contact group writes one row per
/// recipient, so a single SendSmsCommand over a 200-contact group puts 200 rows on this list at
/// once, and the exemption's "bounded by construction" premise is the one thing that is not true of
/// it. The other six exemptions phase 39 uncovered stand unchanged.</para>
///
/// <para>The term matches this row's <b>own</b> columns -- Title, Content and PhoneNumber -- and
/// not the joined ContactName, following ListDealsQuery and ListInvoicesQuery: a search that
/// silently joins on one screen means something different from the same box on every other screen.
/// Content is included because the resolved text is what distinguishes two rows of one batch from
/// each other (merge fields are already substituted), which is the whole reason this list is
/// per-recipient rather than per-send.</para>
/// </summary>
public sealed record ListSmsLogsQuery(
    Guid OrganizationId,
    Guid? ContactId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null)
    : IRequest<SmsLogListDto>, IRequirePermission, IOrganizationScoped, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.SmsLogView;
}

public sealed record SmsLogRowDto(
    Guid Id,
    Guid BatchId,
    Guid ContactId,
    string ContactName,
    string Title,
    string Content,
    string PhoneNumber,
    int CreditsUsed,
    DateTimeOffset SentAt);

public sealed record SmsLogListDto(IReadOnlyList<SmsLogRowDto> Rows, int Page, int PageSize, int TotalCount);
