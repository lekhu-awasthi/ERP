using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.UpdateVariantAttribute;

public sealed class UpdateVariantAttributeCommandValidator : AbstractValidator<UpdateVariantAttributeCommand>
{
    public UpdateVariantAttributeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
