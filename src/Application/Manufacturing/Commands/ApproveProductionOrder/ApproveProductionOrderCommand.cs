using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.ApproveProductionOrder;

/// <summary>Assigns the document number and flips the status. Nothing else: a Production Order is
/// an uncosted plan, so approving one touches no stock, no ledger and no COGS.</summary>
public sealed record ApproveProductionOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveProductionOrderResult>, IRequirePermission, IOrganizationScoped, IRequireFeature,
      ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.ProductionOrderApprove;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    public DocumentType LockDateDocumentType => DocumentType.ProductionOrder;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveProductionOrderResult(Guid Id, string Code, ProductionOrderStatus Status, DateTimeOffset? ApprovedAt);
