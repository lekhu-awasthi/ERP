using FluentValidation;

namespace ErpApp.Application.Pos.Commands.SetPosLocationPaymentModes;

public sealed class SetPosLocationPaymentModesCommandValidator : AbstractValidator<SetPosLocationPaymentModesCommand>
{
    /// <summary>A bound, not a rule: no till shows fifty payment tabs.</summary>
    public const int MaxModes = 50;

    public SetPosLocationPaymentModesCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.PaymentModeIds)
            .NotNull()
            .Must(x => x is null || x.Count <= MaxModes).WithMessage($"At most {MaxModes} payment modes can be linked.")
            .Must(x => x is null || x.All(id => id != Guid.Empty)).WithMessage("A payment mode id is empty.");
    }
}
