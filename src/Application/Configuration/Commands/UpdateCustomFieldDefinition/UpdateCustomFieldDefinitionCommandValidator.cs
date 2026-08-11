using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomFieldDefinition;

public sealed class UpdateCustomFieldDefinitionCommandValidator : AbstractValidator<UpdateCustomFieldDefinitionCommand>
{
    public UpdateCustomFieldDefinitionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.ApplicableDocumentTypes).NotEmpty();
        RuleForEach(x => x.ApplicableDocumentTypes).IsInEnum();
    }
}
