using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;

public sealed class CreateReportingTagCategoryCommandValidator : AbstractValidator<CreateReportingTagCategoryCommand>
{
    public CreateReportingTagCategoryCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}
