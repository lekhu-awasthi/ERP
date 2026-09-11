using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;

/// <summary>Same architecture-spec.md §3.3 pattern as GetInvoiceConversionTemplateQuery, source
/// document is an Approved Invoice instead of a Quotation.</summary>
public sealed record GetCreditNoteConversionTemplateQuery(Guid OrganizationId, Guid InvoiceId)
    : IRequest<CreditNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => InvoiceId;
}

public sealed record CreditNoteConversionTemplateDto(
    Guid ContactId, DateOnly Date, string? Reference, DocumentType ReferrerType, Guid ReferrerId, decimal DiscountPct,
    IReadOnlyList<CreditNoteLineInput> Lines,
    // Phase 35a -- the source document's billing location, carried into the prefill. Without it a
    // Quotation raised at a branch converted to an Invoice at HeadOffice, silently, because the new
    // form's picker falls back to the tenant default. Same shape as the currency the conversion
    // flow already carries verbatim.
    Guid? LocationId);
