using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateCustomStatus;

public sealed class UpdateCustomStatusCommandValidator : AbstractValidator<UpdateCustomStatusCommand>
{
    public UpdateCustomStatusCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentType).IsInEnum();
    }
}
