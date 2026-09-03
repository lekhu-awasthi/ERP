using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PurchaseRegister;

/// <summary>
/// Nepal IRD statutory Purchase Book (Phase 19 decision #3, live-confirmed column-by-column). One
/// row per Approved PurchaseBill (positive) or DebitNote (negative) -- both in the same register.
/// Phase 26c added a separate Purchase Return Register and confirmed live that this one keeps its
/// debit-note rows: the same notes appear in both, negative here and positive there. Both read the
/// shared <c>PurchaseReturnReader</c>, so the magnitudes cannot drift apart.
/// Unlike Sales Register, no domain gap: PurchaseBill already carries IsImport/ExpenditureClassification
/// (Phase 6/8e) -- the exact split this register's 4 value/tax column-pairs need. No Reporting Tag
/// filter -- decision #1 confirmed tags attach only to Quotation/Invoice (Sales side), so there is
/// no intersection to filter on here.
/// </summary>
public sealed record PurchaseRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PurchaseRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseRegisterView;
}

public sealed record PurchaseRegisterRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    string DocumentCode,
    string? ImportDeclarationNo,
    // Nullable since Phase 21c: a migrated register row's party is free text carried over from a
    // prior system (see MigratedSalesRegisterEntry), so it has a name and a PAN but need not
    // resolve to any Contact in this tenant. Every live document row still fills it.
    Guid? ContactId,
    string ContactName,
    string? ContactPan,
    decimal TaxExemptValue,
    decimal TaxableNonCapitalLocalValue,
    decimal TaxableNonCapitalLocalVat,
    decimal TaxableNonCapitalImportValue,
    decimal TaxableNonCapitalImportVat,
    decimal TaxableCapitalValue,
    decimal TaxableCapitalVat);

public sealed record PurchaseRegisterDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<PurchaseRegisterRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalTaxExemptValue,
    decimal TotalTaxableNonCapitalLocalValue,
    decimal TotalTaxableNonCapitalLocalVat,
    decimal TotalTaxableNonCapitalImportValue,
    decimal TotalTaxableNonCapitalImportVat,
    decimal TotalTaxableCapitalValue,
    decimal TotalTaxableCapitalVat);
