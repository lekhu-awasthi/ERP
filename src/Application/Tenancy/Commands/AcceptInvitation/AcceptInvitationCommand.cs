using MediatR;

namespace ErpApp.Application.Tenancy.Commands.AcceptInvitation;

/// <summary>The invited person accepting their own pending invitation (the "Invitation" tab's action).</summary>
public sealed record AcceptInvitationCommand(Guid MembershipId) : IRequest;
