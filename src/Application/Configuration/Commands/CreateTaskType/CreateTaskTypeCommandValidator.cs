using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateTaskType;

public sealed class CreateTaskTypeCommandValidator : AbstractValidator<CreateTaskTypeCommand>
{
    public CreateTaskTypeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).NotEmpty().MaximumLength(20);
    }
}
