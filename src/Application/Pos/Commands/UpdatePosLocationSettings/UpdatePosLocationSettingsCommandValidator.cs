using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos.Commands.UpdatePosLocationSettings;

/// <summary>
/// Phase 60 -- every rule <see cref="PosLocationSettings.Update"/> enforces that can be decided from
/// the request alone, so the caller gets a 400 naming the field rather than the Domain backstop's
/// 500 (phase 39). The default tab depends on the location's stored mode, so the handler raises
/// that one.
/// </summary>
public sealed class UpdatePosLocationSettingsCommandValidator : AbstractValidator<UpdatePosLocationSettingsCommand>
{
    public UpdatePosLocationSettingsCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();

        RuleFor(x => x.ServiceChargeRate)
            .Must(x => x > 0m && x <= 100m)
            .When(x => x.ServiceChargeEnabled)
            .WithMessage("A service charge rate must be more than 0% and at most 100%.");

        RuleFor(x => x.ServiceChargeRate)
            .PrecisionScale(5, 2, ignoreTrailingZeros: true)
            .When(x => x.ServiceChargeEnabled);

        RuleFor(x => x.Denominations)
            .NotNull()
            .Must(x => x is { Count: > 0 }).WithMessage("At least one cash denomination is required.")
            .Must(x => x is null || x.Count <= PosLocationSettings.MaxDenominations)
            .WithMessage($"At most {PosLocationSettings.MaxDenominations} cash denominations are allowed.")
            .Must(x => x is null || x.All(d => d > 0)).WithMessage("Every cash denomination must be a positive amount.")
            .Must(x => x is null || x.Distinct().Count() == x.Count).WithMessage("A cash denomination is listed twice.");

        RuleFor(x => x.DefaultTab).IsInEnum().When(x => x.DefaultTab is not null);
    }
}
