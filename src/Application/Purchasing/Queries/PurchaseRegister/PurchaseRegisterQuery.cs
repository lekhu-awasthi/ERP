using ErpApp.Application.Common.Locations;
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
/// Phase 89: approved Expenses are rows too (positive, typed Expense), since their VAT is input tax the VAT return
/// claims (the user's ruling, 2026-10-12); bills and expenses come from <c>PurchaseBookReader</c>, which the return reads.
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
    bool ExportAll = false,
    // Phase 35b -- the Billing Location filter; null is "All locations". See ILocationFilteredReport.
    Guid? LocationId = null)
    : IRequest<PurchaseRegisterDto>, IRequirePermission, IOrganizationScoped, ILocationFilteredReport
{
    public string PermissionKey => PermissionKeys.PurchaseRegisterView;
}

public sealed record PurchaseRegisterRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    string DocumentCode,
    // Phase 90 -- the purchase book's बीजक नं., as the reference product prints it (read 2026-10-13): the supplier's own
    // invoice number for a bill or an expense (null when none was entered), so IRD can match it against the supplier's
    // sales book; a debit note's own number, since we issue it; a migrated row's Bill No. DocumentCode stays our record.
    string? InvoiceNo,
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
