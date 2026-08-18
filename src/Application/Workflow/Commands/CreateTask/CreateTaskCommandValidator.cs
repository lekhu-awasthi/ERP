using FluentValidation;

namespace ErpApp.Application.Workflow.Commands.CreateTask;

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ParentType).IsInEnum();
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.TaskTypeId).NotEmpty();
        RuleFor(x => x.Priority).IsInEnum();
    }
}
