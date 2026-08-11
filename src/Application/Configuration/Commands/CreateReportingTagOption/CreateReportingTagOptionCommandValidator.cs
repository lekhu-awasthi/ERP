using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagOption;

public sealed class CreateReportingTagOptionCommandValidator : AbstractValidator<CreateReportingTagOptionCommand>
{
    public CreateReportingTagOptionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}
