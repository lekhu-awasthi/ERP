using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;

namespace ErpApp.Application.Configuration.Queries.ListAlertSendLogs;

public sealed class ListAlertSendLogsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListAlertSendLogsQuery, PagedResult<AlertSendLogDto>>
{
    public Task<PagedResult<AlertSendLogDto>> Handle(
        ListAlertSendLogsQuery request, CancellationToken cancellationToken)
    {
        // Left join to the definition (DefaultIfEmpty) rather than an inner join: deleting an alert
        // must not make its send history disappear -- the log is the record that mail left the
        // building, and the reference product keeps showing rows for alerts that no longer exist.
        var query =
            from log in db.AlertSendLogs
            where log.OrganizationId == request.OrganizationId
                  && (request.AlertDefinitionId == null || log.AlertDefinitionId == request.AlertDefinitionId)
            join definition in db.AlertDefinitions on log.AlertDefinitionId equals definition.Id into definitions
            from definition in definitions.DefaultIfEmpty()
            orderby log.CreatedAt descending, log.Recipient
            select new AlertSendLogDto(
                log.Id,
                log.AlertDefinitionId,
                definition != null ? definition.Name : "(deleted alert)",
                log.AlertType,
                log.OccurrenceDate,
                log.Recipient,
                log.Subject,
                log.Status,
                log.FailureReason,
                log.CreatedAt,
                log.CompletedAt);

        return query.ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}
