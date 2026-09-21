using ErpApp.Domain.Imports;
using FluentValidation;

namespace ErpApp.Application.Imports.Commands.CreateImportJob;

public sealed class CreateImportJobCommandValidator : AbstractValidator<CreateImportJobCommand>
{
    private static readonly string[] AllowedExtensions = [".xlsx"];

    /// <summary>
    /// The six upload types this product offers Create New Records for and nothing else.
    ///
    /// <para>Two of them are Phase 21c's migrated registers (a historical statutory row has no
    /// "update" story at all). Two more are Phase 38's trees, <b>and those two are the reference
    /// product's own asymmetry</b>: Phase 21a read its "Select action" dropdown live and found it
    /// offers both modes for five of its seven upload types and Create alone for Product Category
    /// and Account Group. The fifth is the variant importer, whose reasoning is its own -- a
    /// variant's identity is its combination -- and the sixth is phase 45's attribute-pool
    /// importer, whose rows name an existing product and add a pair that is either present or
    /// absent, so there is no second mode with anything different to do.</para>
    /// </summary>
    private static readonly ImportEntityType[] CreateOnlyEntityTypes =
    [
        ImportEntityType.MigratedSalesRegister,
        ImportEntityType.MigratedPurchaseRegister,
        ImportEntityType.ProductCategory,
        ImportEntityType.AccountGroup,
        ImportEntityType.ProductVariant,
        ImportEntityType.ProductAttributePool,

        // Phase 55: a bank statement line has no business key to update *by* -- no document
        // number, no code, nothing a second file could name a row with. See
        // BankStatementLineConfiguration for why it has no natural key at all.
        ImportEntityType.BankStatement,
    ];

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
            .WithMessage("This upload type can only create records, not update them. Choose Create New Records.");

        // Phase 55 -- the one piece of per-run context any import type carries, required for the
        // one type that needs it and refused for the other nine. Both directions matter: without
        // the first the importer throws mid-run on a file the user cannot fix, and without the
        // second an account silently attached to a Product import would read as accepted, which
        // is phase 43's present-and-ignored shape. Asserted in both directions in
        // BankStatementImportSweepGuardTests.
        RuleFor(x => x.BankAccountId)
            .NotNull()
            .When(x => x.EntityType == ImportEntityType.BankStatement)
            .WithMessage("Choose the bank account this statement belongs to.");

        RuleFor(x => x.BankAccountId)
            .Null()
            .When(x => x.EntityType != ImportEntityType.BankStatement)
            .WithMessage("Only a bank statement import names a bank account.");
    }
}
