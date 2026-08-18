using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateDealStage;

public sealed class CreateDealStageCommandValidator : AbstractValidator<CreateDealStageCommand>
{
    public CreateDealStageCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}
