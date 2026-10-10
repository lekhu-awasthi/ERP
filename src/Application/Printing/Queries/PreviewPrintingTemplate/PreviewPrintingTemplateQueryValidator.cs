using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using FluentValidation;

namespace ErpApp.Application.Printing.Queries.PreviewPrintingTemplate;

public sealed class PreviewPrintingTemplateQueryValidator : AbstractValidator<PreviewPrintingTemplateQuery>
{
    public PreviewPrintingTemplateQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentType).NotNull().IsInEnum()
            .Must(type => type is null || DocumentMechanisms.Printable.Contains(type.Value))
            .WithMessage("This document type has no printable record.");
        RuleFor(x => x.Layout).NotNull().IsInEnum();
        RuleFor(x => x.HiddenElements).NotNull();
        RuleForEach(x => x.HiddenElements).IsInEnum();
        RuleFor(x => x.PrintedCustomFieldIds).NotNull();
        RuleForEach(x => x.PrintedCustomFieldIds).NotEmpty();
        RuleFor(x => x.FooterText).MaximumLength(PrintingTemplate.FooterTextMaxLength);
        RuleFor(x => x.CompactMargins).NotNull();
    }
}
