using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Application.Tenancy.Commands.SetOrganizationLogo;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.RemoveOrganizationLogo;

/// <summary>
/// Phase 39 — clears the organization's logo and deletes the blob behind it.
///
/// <para>Same ordering as the replace path in <c>SetOrganizationLogoCommandHandler</c>: the row is
/// committed first and the blob deleted after, so the worst outcome is an orphaned file rather than
/// an organization pointing at bytes that are gone. Removing a logo that is not there is a no-op
/// rather than a 404 — the button's job is to leave the tenant with no logo, and it has.</para>
/// </summary>
public sealed record RemoveOrganizationLogoCommand(Guid OrganizationId)
    : IRequest<OrganizationLogoResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationProfileManage;
}

public sealed class RemoveOrganizationLogoCommandHandler(IAppDbContext db, IFileStorage storage)
    : IRequestHandler<RemoveOrganizationLogoCommand, OrganizationLogoResult>
{
    public async Task<OrganizationLogoResult> Handle(
        RemoveOrganizationLogoCommand request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .FirstOrDefaultAsync(x => x.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        var removed = organization.RemoveLogo();

        if (removed is not null)
        {
            await db.SaveChangesAsync(cancellationToken);
            await storage.DeleteAsync(removed, cancellationToken);
        }

        return new OrganizationLogoResult(organization.Id, false, null);
    }
}
