using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;

/// <summary>Same architecture-spec.md §3.3 pattern as GetCreditNoteConversionTemplateQuery, source
/// document is an Approved PurchaseBill instead of an Invoice.</summary>
public sealed record GetDebitNoteConversionTemplateQuery(Guid OrganizationId, Guid PurchaseBillId)
    : IRequest<DebitNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.PurchaseBillView;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => PurchaseBillId;
}

public sealed record DebitNoteConversionTemplateDto(
    Guid ContactId, DateOnly Date, string? Reference, Guid? TdsTypeId, DocumentType ReferrerType, Guid ReferrerId,
    decimal DiscountPct, IReadOnlyList<DebitNoteLineInput> Lines,
    // Phase 35a -- the source document's billing location, carried into the prefill. Without it a
    // Quotation raised at a branch converted to an Invoice at HeadOffice, silently, because the new
    // form's picker falls back to the tenant default. Same shape as the currency the conversion
    // flow already carries verbatim.
    Guid? LocationId);
