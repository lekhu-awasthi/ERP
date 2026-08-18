using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateDealStage;

public sealed class UpdateDealStageCommandValidator : AbstractValidator<UpdateDealStageCommand>
{
    public UpdateDealStageCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Color).MaximumLength(20);
    }
}
