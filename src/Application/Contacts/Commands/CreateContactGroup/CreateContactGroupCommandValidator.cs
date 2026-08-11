using FluentValidation;

namespace ErpApp.Application.Contacts.Commands.CreateContactGroup;

public sealed class CreateContactGroupCommandValidator : AbstractValidator<CreateContactGroupCommand>
{
    public CreateContactGroupCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
