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
    Guid? LocationId,

    // Phase 39 -- the source document's Terms, carried into the prefill. Phase 27b left this open
    // ("Conversions do not carry terms forward") because the reference behaviour was unconfirmed
    // and the target form's template dropdown restores them in one click. The rule that settles it
    // is the one phase 30 used for Send Email: a conversion carries Terms exactly when *both* ends
    // have the field, which is true of these two pairs and of no other conversion in the codebase
    // -- Purchase Bill, Debit Note and Production Journal have no Terms to receive.
    string? Terms);
