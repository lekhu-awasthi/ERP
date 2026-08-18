using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateTaskType;

public sealed class UpdateTaskTypeCommandValidator : AbstractValidator<UpdateTaskTypeCommand>
{
    public UpdateTaskTypeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).NotEmpty().MaximumLength(20);
    }
}
