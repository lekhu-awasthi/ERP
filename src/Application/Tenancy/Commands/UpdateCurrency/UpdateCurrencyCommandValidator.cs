using FluentValidation;

namespace ErpApp.Application.Tenancy.Commands.UpdateCurrency;

public sealed class UpdateCurrencyCommandValidator : AbstractValidator<UpdateCurrencyCommand>
{
    public UpdateCurrencyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(10);
    }
}
