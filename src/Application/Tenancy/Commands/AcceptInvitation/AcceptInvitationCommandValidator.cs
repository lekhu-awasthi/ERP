using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.AcceptInvitation;

public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.MembershipId).NotEmpty();
    }
}
