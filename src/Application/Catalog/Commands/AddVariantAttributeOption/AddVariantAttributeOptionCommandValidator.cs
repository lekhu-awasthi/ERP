using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.AddVariantAttributeOption;

public sealed class AddVariantAttributeOptionCommandValidator : AbstractValidator<AddVariantAttributeOptionCommand>
{
    public AddVariantAttributeOptionCommandValidator()
    {
        RuleFor(x => x.AttributeId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty().MaximumLength(100);
    }
}
