using ErpApp.Application.Common.Email;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.InviteUser;

public sealed class InviteUserCommandHandler(IAppDbContext db, IEmailSender emailSender, ICurrentUserService currentUser)
    : IRequestHandler<InviteUserCommand, InviteUserResult>
{
    public async Task<InviteUserResult> Handle(InviteUserCommand request, CancellationToken cancellationToken)
    {
        // The "is this user an org admin" check is now AuthorizationBehavior's job
        // (PermissionKeys.OrganizationInviteUser), run before this handler ever executes.
        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        // A system role (OrganizationId null) or this Organization's own custom role -- not some
        // other tenant's custom role.
        var role = await db.Roles.SingleOrDefaultAsync(
            r => r.Id == request.RoleId && (r.OrganizationId == null || r.OrganizationId == request.OrganizationId),
            cancellationToken)
            ?? throw new NotFoundException("Role not found.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var invitedUser = await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        var alreadyInvited = await db.OrganizationMemberships.AnyAsync(
            m => m.OrganizationId == request.OrganizationId
                 && (m.InvitedEmail == normalizedEmail || (invitedUser != null && m.UserId == invitedUser.Id)),
            cancellationToken);

        if (alreadyInvited)
        {
            throw new ConflictException("This user has already been invited to this organization.");
        }

        var membership = OrganizationMembership.Invite(
            request.OrganizationId, invitedUser?.Id, normalizedEmail, role.Id, currentUser.UserId);

        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            normalizedEmail,
            $"You've been invited to join {organization.Name} on ErpApp",
            $"You've been invited to join {organization.Name} as {role.Name}. " +
            "Log in to ErpApp and check your Invitations tab to accept.",
            cancellationToken);

        return new InviteUserResult(membership.Id, normalizedEmail, role.Id, role.Name);
    }
}
