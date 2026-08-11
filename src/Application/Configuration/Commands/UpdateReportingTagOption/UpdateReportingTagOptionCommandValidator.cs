using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagOption;

public sealed class UpdateReportingTagOptionCommandValidator : AbstractValidator<UpdateReportingTagOptionCommand>
{
    public UpdateReportingTagOptionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}
