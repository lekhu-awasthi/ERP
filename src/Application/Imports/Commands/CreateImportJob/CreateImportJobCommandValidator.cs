using ErpApp.Domain.Imports;
using FluentValidation;

namespace ErpApp.Application.Imports.Commands.CreateImportJob;

public sealed class CreateImportJobCommandValidator : AbstractValidator<CreateImportJobCommand>
{
    private static readonly string[] AllowedExtensions = [".xlsx"];

    private static readonly ImportEntityType[] CreateOnlyEntityTypes =
        [ImportEntityType.MigratedSalesRegister, ImportEntityType.MigratedPurchaseRegister];

    public CreateImportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.EntityType).IsInEnum();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(ImportLimits.MaxFileSizeBytes)
            .WithMessage($"File exceeds the {ImportLimits.MaxFileSizeBytes / (1024 * 1024)} MB import size limit.");

        // .xls (the pre-2007 binary format) is rejected rather than silently accepted: ClosedXML
        // cannot read it, so accepting it here would turn a clear 400 at upload into a failed job
        // discovered minutes later.
        RuleFor(x => x.FileName)
            .Must(name => AllowedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only .xlsx files can be imported. Download the template to get the right format.");

        // Phase 21c: the two migrated tax-register types are create-only (see ImportMode). Rejected
        // here, at upload, rather than one identical row error repeated N times -- a whole-file
        // mistake is one mistake. The importers re-check it anyway as defence in depth.
        RuleFor(x => x.Mode)
            .Equal(ImportMode.CreateNew)
            .When(x => CreateOnlyEntityTypes.Contains(x.EntityType))
            .WithMessage("Migrated register rows can only be created, not updated. Choose Create New Records.");
    }
}
