using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.ListAlertSendLogs;

/// <summary>
/// Backs the reference product's "Email Logs" panel (Alert Scheduler's kebab menu -> Email Logs,
/// found live in Phase 20e). Newest first, matching that panel. Optionally narrowed to one alert.
///
/// <para>Genuinely paginated with a real pager, unlike the Configuration lookup screens: this table
/// grows by (definitions x recipients) rows every single day and is the one Configuration list that
/// will realistically reach NFR-5.1's "tens of thousands" framing.</para>
/// </summary>
public sealed record ListAlertSendLogsQuery(
    Guid OrganizationId,
    Guid? AlertDefinitionId = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<AlertSendLogDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AlertSendLogView;
}

public sealed record AlertSendLogDto(
    Guid Id,
    Guid AlertDefinitionId,
    string AlertName,
    AlertType AlertType,
    DateOnly OccurrenceDate,
    string Recipient,
    string Subject,
    AlertSendStatus Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
