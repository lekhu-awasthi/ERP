using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.CreateVariantAttribute;

public sealed class CreateVariantAttributeCommandValidator : AbstractValidator<CreateVariantAttributeCommand>
{
    public CreateVariantAttributeCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        // At least one option: an attribute with no options can never produce a variant, so an
        // empty one is a form the user cannot use rather than a state worth storing.
        RuleFor(x => x.Options).NotEmpty();
        RuleForEach(x => x.Options).NotEmpty().MaximumLength(100);
    }
}
