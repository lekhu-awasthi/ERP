using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;

public sealed class SetTransactionReportingTagsCommandValidator : AbstractValidator<SetTransactionReportingTagsCommand>
{
    public SetTransactionReportingTagsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentType).IsInEnum();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.TagOptionIds).NotNull();
        RuleForEach(x => x.TagOptionIds).NotEmpty();
    }
}
