namespace ErpApp.Domain.Configuration;

/// <summary>
/// The four Custom Templates types confirmed live in the reference product's Configurations >
/// Custom Templates screen (erp-module-scan.md §13; FR-11.3): each is its own accordion section,
/// listing many named merge-field text templates with one marked default per type.
/// </summary>
public enum CustomTemplateType
{
    CustomerBalanceConfirmation,
    SupplierBalanceConfirmation,
    TermsAndConditions,
}
