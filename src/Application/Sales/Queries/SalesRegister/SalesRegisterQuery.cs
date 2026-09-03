using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.SalesRegister;

/// <summary>
/// Nepal IRD statutory Sales Book (Phase 19 decision #3, live-confirmed column-by-column). One row
/// per Approved Invoice (positive) or CreditNote (negative) -- both in the same register, matching
/// the live screen. Phase 26c re-confirmed that live and kept it: the separate Sales Return Register
/// added there lists the <i>same</i> credit notes positively, and this register still carries them
/// negatively with its Total net of them, so the two are two views rather than a split. The
/// credit-note magnitudes now come from the shared <c>SalesReturnReader</c> both reports read.
/// Void/Draft
/// documents never appear (FR-9.10). TagOptionIds narrows to Invoice rows carrying at least one of
/// the given ReportingTagOptions (OR semantics) -- CreditNote rows never carry tags (decision #1),
/// so an active tag filter excludes every CreditNote row, not just unmatched Invoices.
/// </summary>
public sealed record SalesRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? ContactId,
    IReadOnlyList<Guid>? TagOptionIds,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<SalesRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SalesRegisterView;
}

public sealed record SalesRegisterRowDto(
    DateOnly Date,
    DocumentType DocumentType,
    string DocumentCode,
    // Nullable since Phase 21c: a migrated register row's party is free text carried over from a
    // prior system (see MigratedSalesRegisterEntry), so it has a name and a PAN but need not
    // resolve to any Contact in this tenant. Every live document row still fills it.
    Guid? ContactId,
    string ContactName,
    string? ContactPan,
    decimal TotalValue,
    decimal TaxExemptValue,
    decimal TaxableValue,
    decimal VatAmount,
    decimal ExportValue,
    string? ExportCountry,
    string? ExportDeclarationNo,
    DateOnly? ExportDeclarationDate);

public sealed record SalesRegisterDto(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<SalesRegisterRowDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalValue,
    decimal TotalTaxExemptValue,
    decimal TotalTaxableValue,
    decimal TotalVatAmount);
