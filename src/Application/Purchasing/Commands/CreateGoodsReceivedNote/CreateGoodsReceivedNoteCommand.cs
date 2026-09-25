using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateGoodsReceivedNote;

/// <summary>Phase 58 -- creates a Draft Goods Received Note. <paramref name="ReferrerType"/> /
/// <paramref name="ReferrerId"/> name the Purchase Order it is received against, if any; the handler
/// checks the order and marks it received (phase 6's rule: a referrer enforces nothing on its
/// own).</summary>
public sealed record CreateGoodsReceivedNoteCommand(
    Guid OrganizationId, Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference, string? TrackingNo,
    IReadOnlyList<GoodsReceivedNoteLineInput> Lines, decimal DiscountPct = 0,
    DocumentType? ReferrerType = null, Guid? ReferrerId = null)
    : IRequest<CreateGoodsReceivedNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteCreate;

    /// <summary>Phase 28's currency pair -- see <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Phase 32's billing location -- see <see cref="ILocationBearingCommand"/>.</summary>
    public Guid? LocationId { get; init; }

    public DocumentType AuditDocumentType => DocumentType.GoodsReceivedNote;
}

public sealed record CreateGoodsReceivedNoteResult(Guid Id, string Code, GoodsReceivedNoteStatus Status);
