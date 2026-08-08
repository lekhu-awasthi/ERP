using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.AcceptRequest;

public sealed class AcceptRequestCommandValidator : AbstractValidator<AcceptRequestCommand>
{
    public AcceptRequestCommandValidator()
    {
        RuleFor(x => x.MembershipId).NotEmpty();
    }
}
