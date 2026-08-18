using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateLeadSource;

public sealed class UpdateLeadSourceCommandValidator : AbstractValidator<UpdateLeadSourceCommand>
{
    public UpdateLeadSourceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
