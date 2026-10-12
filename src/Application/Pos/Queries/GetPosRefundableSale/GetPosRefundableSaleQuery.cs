using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.GetPosRefundableSale;

/// <param name="Sold">The quantity on the sale line, in its own unit.</param>
/// <param name="Remaining">What can still be refunded: sold, less what earlier credit notes returned.</param>
public sealed record PosRefundableLineDto(
    Guid InvoiceLineId,
    Guid ProductId,
    string ProductName,
    string UnitShortName,
    decimal Sold,
    decimal Remaining,
    decimal Rate,
    decimal DiscountPct,
    decimal ServiceChargeRate,
    decimal PackagingFeeAmount,
    VatRate VatRate,
    decimal LineTotal,
    decimal DiscountPerUnit);

public sealed record PosPriorRefundDto(Guid CreditNoteId, string Code, DateOnly Date, decimal GrandTotal);

/// <summary>
/// Phase 63 -- a till sale as the refund screen needs it: its lines with what is left to refund, the
/// refunds already made, and what the customer still owes on it.
/// </summary>
/// <param name="Settled">What was paid at the till: tendered less change.</param>
/// <param name="Owed">What the customer still owes on this sale now; a refund comes off this first.</param>
/// <param name="CanRefund">Whether the caller holds <c>Sales.CreditNote.Create</c> and <c>.Approve</c> at the till's
/// location, which every refund needs (Decision E), so the screen can say so before the cashier picks lines.</param>
/// <param name="SoldAtLocationName">Phase 84 -- the selling location's name when it is not this till's; null for a
/// sale of this location.</param>
/// <param name="ReturnWarehouseName">Phase 84 -- where the returned goods go back into stock: the sale's warehouse at
/// home, this till's default warehouse for another branch's sale.</param>
public sealed record PosRefundableSaleDto(
    Guid InvoiceId,
    string Code,
    DateOnly Date,
    DateTimeOffset? SoldAt,
    Guid? LocationId,
    string? SessionCode,
    Guid ContactId,
    string CustomerName,
    bool IsWalkIn,
    decimal GrandTotal,
    decimal Settled,
    decimal Owed,
    IReadOnlyList<PosRefundableLineDto> Lines,
    IReadOnlyList<PosPriorRefundDto> PriorRefunds,
    bool CanRefund,
    string? SoldAtLocationName,
    string? ReturnWarehouseName);

/// <summary>
/// A sale as the till at <see cref="LocationId"/> would refund it. Phase 84 moved it under the till: a sale of this
/// location is read under <c>Sales.Invoice.View</c> here, like its receipt; another location's (phase 84, Decision B)
/// under the refund's own two keys here, <c>Sales.CreditNote.Create</c> and <c>.Approve</c>, with no grant at the
/// selling branch -- the customer is holding the bill. Both are re-checked in the handler, because which applies
/// depends on the loaded sale (the <c>AttachmentAccess</c> shape); the pipeline admits a caller holding View at any
/// location (<see cref="ILocationFilteredQuery"/>).
/// </summary>
public sealed record GetPosRefundableSaleQuery(Guid OrganizationId, Guid LocationId, Guid InvoiceId)
    : IRequest<PosRefundableSaleDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILocationFilteredQuery
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
