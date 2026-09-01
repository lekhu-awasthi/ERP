using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Tenancy.Commands.UpdateAiDocumentExtractionSetting;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetAiDocumentExtractionSetting;

/// <summary>
/// Reads the tenant's extraction consent, plus whether the deployment has a credential at all.
///
/// <para>Gated on <c>InboxDocumentView</c>, not on the Admin-only
/// <c>AiDocumentExtractionManage</c>: every user of the inbox needs to know why the Extract button
/// is or is not offered, and "is this switched on?" is not itself sensitive. Changing it stays
/// Admin-only.</para>
/// </summary>
public sealed record GetAiDocumentExtractionSettingQuery(Guid OrganizationId)
    : IRequest<AiDocumentExtractionSettingDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InboxDocumentView;
}

public sealed class GetAiDocumentExtractionSettingQueryHandler(IAppDbContext db, IDocumentExtractor extractor)
    : IRequestHandler<GetAiDocumentExtractionSettingQuery, AiDocumentExtractionSettingDto>
{
    public async Task<AiDocumentExtractionSettingDto> Handle(
        GetAiDocumentExtractionSettingQuery request, CancellationToken cancellationToken)
    {
        var enabled = await db.TenantSettings
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Select(x => x.AiDocumentExtractionEnabled)
            .SingleOrDefaultAsync(cancellationToken);

        return new AiDocumentExtractionSettingDto(enabled, extractor.IsConfigured, extractor.ModelId);
    }
}
