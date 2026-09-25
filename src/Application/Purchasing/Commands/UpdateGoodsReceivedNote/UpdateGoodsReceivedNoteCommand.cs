using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdateGoodsReceivedNote;

/// <summary>Phase 58 -- edits a Draft Goods Received Note. Draft-only, like every document here: the
/// reference product also offers Edit on an approved GRN, and this codebase does not, for the reason
/// it does not on any other document -- an approved document's number and stock effect are facts
/// other records already rely on. The Purchase Order referrer is fixed at create and not editable.</summary>
public sealed record UpdateGoodsReceivedNoteCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference,
    string? TrackingNo, IReadOnlyList<GoodsReceivedNoteLineInput> Lines, decimal DiscountPct = 0)
    : IRequest<UpdateGoodsReceivedNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand, ILocationBearingCommand, ILocationScopedDocument
{
    public string PermissionKey => PermissionKeys.GoodsReceivedNoteEdit;

    /// <summary>Phase 32b -- a location-scoped caller must hold the key at this document location.</summary>
    public Guid LocationDocumentId => Id;

    /// <inheritdoc cref="Commands.CreateGoodsReceivedNote.CreateGoodsReceivedNoteCommand.CurrencyCode"/>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <inheritdoc cref="Commands.CreateGoodsReceivedNote.CreateGoodsReceivedNoteCommand.LocationId"/>
    public Guid? LocationId { get; init; }

    public DocumentType AuditDocumentType => DocumentType.GoodsReceivedNote;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateGoodsReceivedNoteResult(Guid Id, string Code, GoodsReceivedNoteStatus Status);
