using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Communications.Commands.CreateEmailTemplate;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Queries.ListEmailTemplates;

/// <summary>
/// The Configurations &gt; Custom Templates &gt; Email panel. <paramref name="Context"/> null
/// returns every context, which is what the panel itself shows — one card per template, each
/// labelled with its context, exactly as live.
/// </summary>
public sealed record ListEmailTemplatesQuery(Guid OrganizationId, EmailTemplateContext? Context, bool IncludeInactive)
    : IRequest<ListEmailTemplatesResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailTemplateView;
}

/// <param name="MergeFields">The catalogue for the requested context, or for every context when
/// none was named — this is what drives the editor's "Custom Tags" menu, so the client never
/// hard-codes a token list that could drift from the resolver's.</param>
public sealed record ListEmailTemplatesResult(
    IReadOnlyList<EmailTemplateDto> Templates,
    IReadOnlyList<EmailTemplateContextDto> Contexts,
    IReadOnlyList<EmailMergeField> MergeFields);

public sealed record EmailTemplateContextDto(EmailTemplateContext Context, string Name);

public sealed class ListEmailTemplatesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListEmailTemplatesQuery, ListEmailTemplatesResult>
{
    public async Task<ListEmailTemplatesResult> Handle(
        ListEmailTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = db.EmailTemplates.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.Context is not null)
        {
            query = query.Where(x => x.Context == request.Context.Value);
        }

        if (!request.IncludeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var templates = await query
            .OrderBy(x => x.Context)
            .ThenByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var contexts = Enum.GetValues<EmailTemplateContext>()
            .Select(x => new EmailTemplateContextDto(x, EmailMergeFields.GroupNameFor(x)))
            .ToList();

        var mergeFields = request.Context is not null
            ? EmailMergeFields.For(request.Context.Value)
            : contexts.SelectMany(x => EmailMergeFields.For(x.Context))
                .DistinctBy(x => x.Token, StringComparer.Ordinal)
                .ToList();

        return new ListEmailTemplatesResult(
            templates.Select(EmailTemplateMapping.ToDto).ToList(),
            contexts,
            mergeFields);
    }
}
