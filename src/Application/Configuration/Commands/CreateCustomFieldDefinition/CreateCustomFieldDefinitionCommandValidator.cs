using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;

public sealed class CreateCustomFieldDefinitionCommandValidator : AbstractValidator<CreateCustomFieldDefinitionCommand>
{
    public CreateCustomFieldDefinitionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.ApplicableDocumentTypes).NotEmpty();
        RuleForEach(x => x.ApplicableDocumentTypes).IsInEnum();
    }
}
