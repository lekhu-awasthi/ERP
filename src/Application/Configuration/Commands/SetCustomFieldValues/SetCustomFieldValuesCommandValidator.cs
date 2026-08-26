using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.SetCustomFieldValues;

public sealed class SetCustomFieldValuesCommandValidator : AbstractValidator<SetCustomFieldValuesCommand>
{
    public SetCustomFieldValuesCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Values).NotNull();
        RuleForEach(x => x.Values).ChildRules(value =>
        {
            value.RuleFor(v => v.FieldDefinitionId).NotEmpty();
            value.RuleFor(v => v.Value).NotNull().MaximumLength(1000);
        });
    }
}
