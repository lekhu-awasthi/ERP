using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;

public sealed class SetProductVariantAttributesCommandValidator
    : AbstractValidator<SetProductVariantAttributesCommand>
{
    public SetProductVariantAttributesCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleForEach(x => x.Usages).ChildRules(u =>
        {
            u.RuleFor(x => x.AttributeId).NotEmpty();
            u.RuleFor(x => x.OptionId).NotEmpty();
        });
    }
}
