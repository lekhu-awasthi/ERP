using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttributeOption;

public sealed class UpdateVariantAttributeOptionCommandValidator : AbstractValidator<UpdateVariantAttributeOptionCommand>
{
    public UpdateVariantAttributeOptionCommandValidator()
    {
        RuleFor(x => x.AttributeId).NotEmpty();
        RuleFor(x => x.OptionId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty().MaximumLength(100);
    }
}
