using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Sales.Queries.ListCreditableInvoices;

/// <summary>
/// Phase 69 -- the credit-note form's invoice picker: one customer's approved ERP invoices, newest first,
/// each with what is still left to credit. The reference product's picker reads
/// <c>/invoices?items=true&amp;referred=false&amp;contact_id=…&amp;status=Approved</c> and hides an invoice
/// once any note names it (erp-module-scan.md, phase 69 appendix), so a second partial credit cannot be
/// raised there; here an invoice stays listed until its value is used up, and a fully credited one is
/// listed with nothing remaining so a user searching for it learns why it cannot be chosen.
///
/// <para><b>Permission (derived, phase-69-status.md Decision G):</b> <c>Sales.CreditNote.Create</c>, not
/// the invoice list's View key. The picker is part of creating a note, and it shows exactly what the note
/// will print about the invoice -- number, date, total -- to someone allowed to create that note for that
/// customer. A list, so a location-scoped caller sees fewer rows, never a 403
/// (<see cref="ILocationFilteredQuery"/>).</para>
///
/// <para>Till sales are left out: they are returned at the till (phase 63 Decision G), and a price
/// adjustment against one is refused for the same reason.</para>
/// </summary>
public sealed record ListCreditableInvoicesQuery(
    Guid OrganizationId,
    Guid ContactId,
    Guid? LocationId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    // The draft being edited: its own saved credit is about to be replaced, so it is not counted
    // against the invoice it names -- the exclusion the server's value cap makes too.
    Guid? ExcludingCreditNoteId = null)
    : IRequest<PagedResult<CreditableInvoiceDto>>, IRequirePermission, IOrganizationScoped, ILocationFilteredQuery, ISearchableQuery
{
    public string PermissionKey => PermissionKeys.CreditNoteCreate;
}

/// <param name="CreditedTotal">What the non-void notes against it already credit, returns and price
/// adjustments together, drafts included -- the same sum the server's value cap reads.</param>
public sealed record CreditableInvoiceDto(
    Guid Id,
    string Code,
    DateOnly Date,
    Guid? LocationId,
    string CurrencyCode,
    decimal ExchangeRate,
    decimal GrandTotal,
    decimal CreditedTotal,
    decimal RemainingTotal);
