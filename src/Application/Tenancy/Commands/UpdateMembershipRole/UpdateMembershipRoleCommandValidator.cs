using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.UpdateMembershipRole;

public sealed class UpdateMembershipRoleCommandValidator : AbstractValidator<UpdateMembershipRoleCommand>
{
    public UpdateMembershipRoleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.MembershipId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}
