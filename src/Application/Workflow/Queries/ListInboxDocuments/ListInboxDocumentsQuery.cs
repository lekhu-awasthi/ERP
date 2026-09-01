using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Workflow;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.ListInboxDocuments;

/// <summary>
/// The Document inbox grid, newest first. Paginated server-side from the start (NFR-5.1).
///
/// <para>Two filters, and they serve two different screens from one query. <paramref name="Status"/>
/// backs the inbox's own Pending/Done tabs. <paramref name="LinkedTransactionType"/> +
/// <paramref name="LinkedTransactionId"/> back the <b>source-document panel on a transaction's
/// detail page</b>, which is exit criterion #2 -- "the source image stays linked and viewable from
/// the resulting document" is a requirement on the transaction screen, not only on the inbox.</para>
/// </summary>
public sealed record ListInboxDocumentsQuery(
    Guid OrganizationId,
    UploadedDocumentStatus? Status = null,
    DocumentType? LinkedTransactionType = null,
    Guid? LinkedTransactionId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<InboxDocumentDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentView;
}

public sealed class ListInboxDocumentsQueryValidator : AbstractValidator<ListInboxDocumentsQuery>
{
    public ListInboxDocumentsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Search).MaximumLength(260);
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}

public sealed class ListInboxDocumentsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListInboxDocumentsQuery, PagedResult<InboxDocumentDto>>
{
    public async Task<PagedResult<InboxDocumentDto>> Handle(
        ListInboxDocumentsQuery request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        // String.Contains, not EF.Functions.Like -- the InMemory provider cannot translate Like at
        // all, while SQL Server turns Contains into the same LIKE '%term%'. Case-insensitivity comes
        // from the database collation, as everywhere else in this tree.
        var query =
            from document in db.UploadedDocuments
            where document.OrganizationId == request.OrganizationId
                && (request.Status == null || document.Status == request.Status)
                && (request.LinkedTransactionType == null
                    || document.LinkedTransactionType == request.LinkedTransactionType)
                && (request.LinkedTransactionId == null
                    || document.LinkedTransactionId == request.LinkedTransactionId)
                && (search == null
                    || document.FileName.Contains(search)
                    || (document.Description != null && document.Description.Contains(search))
                    || (document.Label != null && document.Label.Contains(search)))
            join user in db.Users on document.UploadedByUserId equals user.Id into uploaders
            from uploader in uploaders.DefaultIfEmpty()
            orderby document.UploadedAt descending, document.Id descending
            select new { Document = document, UploaderName = uploader == null ? string.Empty : uploader.FullName };

        var totalCount = await query.CountAsync(cancellationToken);

        var page = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InboxDocumentDto>(
            [.. page.Select(x => InboxDocumentMapper.ToDto(x.Document, x.UploaderName))],
            request.Page,
            request.PageSize,
            totalCount);
    }
}
