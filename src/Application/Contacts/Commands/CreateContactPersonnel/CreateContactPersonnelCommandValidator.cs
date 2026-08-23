using FluentValidation;

namespace ErpApp.Application.Contacts.Commands.CreateContactPersonnel;

public sealed class CreateContactPersonnelCommandValidator : AbstractValidator<CreateContactPersonnelCommand>
{
    public CreateContactPersonnelCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Code).MaximumLength(30);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.OrganizationTitle).MaximumLength(100);
    }
}
