using FluentValidation;

namespace ErpApp.Application.Pos.Commands.OpenPosSession;

public sealed class OpenPosSessionCommandValidator : AbstractValidator<OpenPosSessionCommand>
{
    public OpenPosSessionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.OpeningAmount).PosCashAmount();
        RuleFor(x => x.Denominations).PosDenominations();
    }
}
