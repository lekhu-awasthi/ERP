using FluentValidation;

namespace ErpApp.Application.Contacts.Commands.UpdateContactPersonnel;

public sealed class UpdateContactPersonnelCommandValidator : AbstractValidator<UpdateContactPersonnelCommand>
{
    public UpdateContactPersonnelCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.Code).MaximumLength(30);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.OrganizationTitle).MaximumLength(100);
    }
}
