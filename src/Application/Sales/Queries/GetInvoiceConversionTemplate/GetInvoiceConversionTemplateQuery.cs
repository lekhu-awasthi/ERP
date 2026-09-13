using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetInvoiceConversionTemplate;

/// <summary>
/// architecture-spec.md §3.3's document-conversion pattern -- server-computes a pre-filled
/// CreateInvoiceCommand-shaped DTO from an Approved Quotation. Not its own domain command: the
/// Angular client still POSTs a normal CreateInvoiceCommand afterward (see SalesEndpoints'
/// "Convert to Invoice" flow's doc comment) -- this query has no side effects and no audit trail
/// of its own.
/// </summary>
public sealed record GetInvoiceConversionTemplateQuery(Guid OrganizationId, Guid QuotationId)
    : IRequest<InvoiceConversionTemplateDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.QuotationView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => QuotationId;
}

public sealed record InvoiceConversionTemplateDto(
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    DocumentType ReferrerType,
    Guid ReferrerId,
    decimal DiscountPct,
    IReadOnlyList<InvoiceLineInput> Lines,
    // Phase 35a -- the source document's billing location, carried into the prefill. Without it a
    // Quotation raised at a branch converted to an Invoice at HeadOffice, silently, because the new
    // form's picker falls back to the tenant default. Same shape as the currency the conversion
    // flow already carries verbatim.
    Guid? LocationId,

    // Phase 39 -- the source document's Terms, carried into the prefill. Phase 27b left this open
    // ("Conversions do not carry terms forward") because the reference behaviour was unconfirmed
    // and the target form's template dropdown restores them in one click. The rule that settles it
    // is the one phase 30 used for Send Email: a conversion carries Terms exactly when *both* ends
    // have the field, which is true of these two pairs and of no other conversion in the codebase
    // -- Purchase Bill, Debit Note and Production Journal have no Terms to receive.
    string? Terms);
