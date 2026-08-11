using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagCategory;

public sealed class UpdateReportingTagCategoryCommandValidator : AbstractValidator<UpdateReportingTagCategoryCommand>
{
    public UpdateReportingTagCategoryCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
