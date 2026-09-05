using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;

public sealed record ApprovePurchaseBillCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApprovePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.PurchaseBillApprove;
    public DocumentType LockDateDocumentType => DocumentType.PurchaseBill;
    public Guid LockDateDocumentId => Id;
}

/// <summary>Phase 29 (FR-6.15) adds the two landed-cost figures, both in base currency and both
/// null when the bill carried no Additional Cost section: what the FIFO layers actually absorbed,
/// and the rounding residue that did not fit into them (see
/// <see cref="Domain.Purchasing.PurchaseBill.CapitalisedAdditionalCost"/>). Returned rather than
/// left to a follow-up read so the residue is visible at the moment it is created.</summary>
public sealed record ApprovePurchaseBillResult(
    Guid Id,
    string Code,
    PurchaseBillStatus Status,
    DateTimeOffset? ApprovedAt,
    decimal? CapitalisedAdditionalCost = null,
    decimal? AdditionalCostRoundingAdjustment = null);
