using FluentValidation;

namespace ErpApp.Application.Pos.Commands.SetLocationPosMode;

public sealed class SetLocationPosModeCommandValidator : AbstractValidator<SetLocationPosModeCommand>
{
    public SetLocationPosModeCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.PosMode).IsInEnum();
    }
}
