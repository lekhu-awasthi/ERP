using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Communications;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Queries.ListEmailLogs;

/// <summary>
/// Backs the Email Logs sub-tab — the fourth on a Contact's Activity tab, the third ("Emails") on a
/// document's. One query for both, keyed by <see cref="EmailParentType"/> exactly as the reference
/// product's own <c>/email-logs?source=&amp;source_id=</c> is (docs/phase-30-status.md, Step 1.5).
///
/// <para>Phase 27b built this tab's pager against no data at all — the tab existed and rendered an
/// empty-state message because there was no entity behind it. This is that entity.</para>
///
/// <para>Rides <see cref="PermissionKeys.EmailLogView"/>, Admin+Member, and the handler re-checks
/// the parent's own View key — the same two layers as sending, so a user cannot read what was
/// emailed about a document they may not open.</para>
/// </summary>
public sealed record ListEmailLogsQuery(
    Guid OrganizationId, DocumentType? DocumentType, Guid ParentId, int Page, int PageSize)
    : IRequest<PagedResult<EmailLogRowDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailLogView;
}

/// <param name="Recipients">The To list, comma-joined for display — the live grid shows recipients
/// as one cell.</param>
/// <param name="AttachmentNames">The PDF's name where one was attached, then each dropped file.
/// Survives the blobs being purged, which is the point.</param>
public sealed record EmailLogRowDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string Recipients,
    string? Cc,
    string? Bcc,
    string Subject,
    EmailSendStatus Status,
    string? FailureReason,
    string SentByUserName,
    bool AttachedDocumentPdf,
    IReadOnlyList<string> AttachmentNames);

public sealed class ListEmailLogsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListEmailLogsQuery, PagedResult<EmailLogRowDto>>
{
    public async Task<PagedResult<EmailLogRowDto>> Handle(
        ListEmailLogsQuery request, CancellationToken cancellationToken)
    {
        await EmailComposition.EnsureMayEmailParentAsync(
            db, request.OrganizationId, currentUser.UserId, request.DocumentType, cancellationToken);

        var parentType = request.DocumentType is null
            ? EmailParentType.Contact
            : DocumentParentTypes.For<EmailParentType>(request.DocumentType.Value);

        var query = db.EmailSendLogs
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.ParentType == parentType
                        && x.ParentId == request.ParentId);

        var totalCount = await query.CountAsync(cancellationToken);

        var logs = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(x => x.Attachments)
            .ToListAsync(cancellationToken);

        var userIds = logs.Select(x => x.SentByUserId).Distinct().ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var rows = logs.Select(x => new EmailLogRowDto(
            x.Id,
            x.CreatedAt,
            x.CompletedAt,
            string.Join(", ", x.To),
            x.CcAddresses,
            x.BccAddresses,
            x.Subject,
            x.Status,
            x.FailureReason,
            userNames.GetValueOrDefault(x.SentByUserId, string.Empty),
            x.AttachDocumentPdf,
            x.Attachments.OrderBy(a => a.FileName).Select(a => a.FileName).ToList()))
            .ToList();

        return new PagedResult<EmailLogRowDto>(rows, request.Page, request.PageSize, totalCount);
    }
}
