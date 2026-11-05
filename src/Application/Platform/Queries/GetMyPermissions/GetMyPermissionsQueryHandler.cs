using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Platform.Queries.GetMyPermissions;

public sealed class GetMyPermissionsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyPermissionsQuery, MyPermissionsDto>
{
    public async Task<MyPermissionsDto> Handle(GetMyPermissionsQuery request, CancellationToken cancellationToken)
    {
        // Both reads go through GrantedPermissionReader, the join AuthorizationBehavior performs, so
        // the shell's answer cannot drift from the pipeline's: Accepted memberships, IsGranted rows.
        var organizationWide = await GrantedPermissionReader.GrantedKeysAsync(
            db, request.OrganizationId, currentUser.UserId, cancellationToken);

        var atSomeLocation = await GrantedPermissionReader.LocationGrantedKeysAsync(
            db, request.OrganizationId, currentUser.UserId, cancellationToken);

        return new MyPermissionsDto(
            organizationWide.Order(StringComparer.Ordinal).ToList(),
            atSomeLocation.Where(x => !organizationWide.Contains(x)).Order(StringComparer.Ordinal).ToList());
    }
}
