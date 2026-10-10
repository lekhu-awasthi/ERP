using ErpApp.Domain.Configuration;
using FluentValidation;

namespace ErpApp.Application.Configuration.Commands.UpdatePrintingTemplateLayout;

/// <summary>Phase 73 -- the shape. Whether each element may be hidden depends on the stored template's type, so the
/// handler checks that (still as a 400 naming <c>HiddenElements</c>), with the Domain as the backstop.</summary>
public sealed class UpdatePrintingTemplateLayoutCommandValidator : AbstractValidator<UpdatePrintingTemplateLayoutCommand>
{
    public UpdatePrintingTemplateLayoutCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Layout).NotNull().IsInEnum();
        RuleFor(x => x.HiddenElements).NotNull();
        RuleForEach(x => x.HiddenElements).IsInEnum();
        RuleFor(x => x.PrintedCustomFieldIds).NotNull();
        RuleForEach(x => x.PrintedCustomFieldIds).NotEmpty();
        RuleFor(x => x.FooterText).MaximumLength(PrintingTemplate.FooterTextMaxLength);
        RuleFor(x => x.CompactMargins).NotNull();
    }
}
