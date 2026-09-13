using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetOrganizationLogo;

/// <summary>
/// Phase 39 — the logo's bytes, for the profile screen's <c>&lt;img&gt;</c> and for the print
/// pipeline's header.
///
/// <para>The content type comes from the column, which was written from the file's own header bytes
/// at upload (<c>ImageHeader</c>) — never from what the uploader's multipart part claimed. That is
/// what makes it safe to send as a <c>Content-Type</c> to a browser.</para>
/// </summary>
public sealed record GetOrganizationLogoQuery(Guid OrganizationId)
    : IRequest<OrganizationLogoContent>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationProfileView;
}

/// <param name="Content">The caller disposes it.</param>
public sealed record OrganizationLogoContent(Stream Content, string ContentType);

public sealed class GetOrganizationLogoQueryHandler(IAppDbContext db, IFileStorage storage)
    : IRequestHandler<GetOrganizationLogoQuery, OrganizationLogoContent>
{
    public async Task<OrganizationLogoContent> Handle(
        GetOrganizationLogoQuery request, CancellationToken cancellationToken)
    {
        var logo = await db.Organizations
            .Where(x => x.Id == request.OrganizationId)
            .Select(x => new { x.LogoStorageKey, x.LogoContentType })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        if (logo.LogoStorageKey is null)
        {
            throw new NotFoundException("This organization has no logo.");
        }

        var content = await storage.OpenReadAsync(logo.LogoStorageKey, cancellationToken);

        return new OrganizationLogoContent(content, logo.LogoContentType ?? "application/octet-stream");
    }
}
