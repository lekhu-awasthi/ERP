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
        var organization = await db.Organizations.SingleOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        var isAdmin = await db.OrganizationMemberships.AnyAsync(
            m => m.OrganizationId == request.OrganizationId
                 && m.UserId == currentUser.UserId
                 && m.Status == MembershipStatus.Accepted
                 && m.Role == MembershipRole.Admin,
            cancellationToken);

        if (!isAdmin)
        {
            throw new ForbiddenException("Only an organization admin can invite users.");
        }

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
            request.OrganizationId, invitedUser?.Id, normalizedEmail, request.Role, currentUser.UserId);

        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            normalizedEmail,
            $"You've been invited to join {organization.Name} on ErpApp",
            $"You've been invited to join {organization.Name} as {request.Role}. " +
            "Log in to ErpApp and check your Invitations tab to accept.",
            cancellationToken);

        return new InviteUserResult(membership.Id, normalizedEmail, request.Role);
    }
}
