using FluentValidation;

namespace ErpApp.Application.Crm.Commands.MoveDealToStage;

public sealed class MoveDealToStageCommandValidator : AbstractValidator<MoveDealToStageCommand>
{
    public MoveDealToStageCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DealStageId).NotEmpty();
    }
}
