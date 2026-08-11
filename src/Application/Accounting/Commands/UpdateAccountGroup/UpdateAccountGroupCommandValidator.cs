using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.UpdateAccountGroup;

public sealed class UpdateAccountGroupCommandValidator : AbstractValidator<UpdateAccountGroupCommand>
{
    public UpdateAccountGroupCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
