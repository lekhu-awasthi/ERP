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
/// </summary>
public sealed record ListSmsLogsQuery(Guid OrganizationId, Guid? ContactId, int Page = 1, int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<SmsLogListDto>, IRequirePermission, IOrganizationScoped
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
