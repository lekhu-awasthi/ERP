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
    Guid? LocationId,
    // Phase 43 (37 carried item #1) -- the source bill's warehouse, carried into the prefill for the
    // same reason the location above is: the new form would otherwise show an empty picker for a
    // fact the conversion already knows. The server defaults to this value anyway when the command
    // arrives with a null warehouse, so the prefill is what makes the form honest rather than what
    // makes it correct.
    Guid WarehouseId,

    // Phase 69 -- the source document's currency and rate, carried into the prefill. The comment on
    // LocationId above (phase 35a) took the currency to be carried already; it never was, so a USD
    // source converted to a base-currency document at rate 1 and booked its foreign amounts as
    // rupees. The reference product carries both verbatim (erp-module-scan.md:413).
    string CurrencyCode,
    decimal ExchangeRate);
