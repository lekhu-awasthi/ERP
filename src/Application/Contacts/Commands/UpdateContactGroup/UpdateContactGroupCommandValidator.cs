using FluentValidation;

namespace ErpApp.Application.Contacts.Commands.UpdateContactGroup;

public sealed class UpdateContactGroupCommandValidator : AbstractValidator<UpdateContactGroupCommand>
{
    public UpdateContactGroupCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
