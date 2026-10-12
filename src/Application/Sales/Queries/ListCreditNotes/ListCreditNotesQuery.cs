using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListCreditNotes;

/// <summary>
/// Phase 86 -- one row of the list: the header fields the page shows, plus <see cref="ReturnWarehouseName"/>, which the
/// aggregate cannot carry. Every other document list still returns its aggregate; this one stopped when a row needed a
/// figure read from other rows (<c>CreditNoteReturnWarehouses</c>). A new header field must be added here to reach the
/// list (phase 32's detail-DTO gotcha, now true of this list too).
/// </summary>
public sealed record CreditNoteListItemDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    string? Reference,
    CreditNoteStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    decimal DiscountPct,
    string? Terms,
    Guid? LocationId,
    Guid? AgainstInvoiceId,
    string? AgainstInvoiceNumber,
    DateOnly? AgainstInvoiceDate,
    string? Reason,
    string? ReturnWarehouseName);

public sealed record ListCreditNotesQuery(
    Guid OrganizationId,
    CreditNoteStatus? Status,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    // Phase 35a -- the list's own Billing Location filter, the control the reference product
    // renders as a funnel on the LOCATION column every document grid carries (confirm-live
    // 2026-09-10). Independent of phase 32b's permission scope above it: a caller may hold every
    // location and still want one branch's rows. Optional and trailing, so null is "All" and
    // every pre-phase-35 caller keeps its behaviour.
    Guid? LocationId = null,
    // Phase 47 -- the chrome's `Sort by`, swept from the one consumer phase 40 proved the seam
    // with. Optional and trailing, so null is this list's own default and every existing caller
    // keeps its behaviour. The two orderings are the two indexes `TenantIndexConvention` gives a
    // document -- see `ISortableQuery` for why that, and not taste, is what decides the menu.
    string? Sort = null)
    : IRequest<PagedResult<CreditNoteListItemDto>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.CreditNoteView;
}
