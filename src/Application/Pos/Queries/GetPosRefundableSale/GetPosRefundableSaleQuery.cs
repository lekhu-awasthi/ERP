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
    VatRate VatRate,
    decimal LineTotal);

public sealed record PosPriorRefundDto(Guid CreditNoteId, string Code, DateOnly Date, decimal GrandTotal);

/// <summary>
/// Phase 63 -- a till sale as the refund screen needs it: its lines with what is left to refund, the
/// refunds already made, and what the customer still owes on it.
/// </summary>
/// <param name="Settled">What was paid at the till: tendered less change.</param>
/// <param name="Owed">What the customer still owes on this sale now; a refund comes off this first.</param>
/// <param name="CanRefund">Whether the caller holds <c>Sales.CreditNote.Approve</c> at the sale's location,
/// which every refund needs (Decision E), so the screen can say so before the cashier picks lines.</param>
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
    bool CanRefund);

/// <summary>Read under <c>Sales.Invoice.View</c> at the sale's location, like its receipt.</summary>
public sealed record GetPosRefundableSaleQuery(Guid OrganizationId, Guid InvoiceId)
    : IRequest<PosRefundableSaleDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.InvoiceView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;

    public Guid LocationDocumentId => InvoiceId;
}
