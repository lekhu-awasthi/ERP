using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.VoidProductionOrder;

public sealed record VoidProductionOrderCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidProductionOrderResult>, IRequirePermission, IOrganizationScoped, IRequireFeature,
      ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.ProductionOrderVoid;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];

    public DocumentType LockDateDocumentType => DocumentType.ProductionOrder;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidProductionOrderResult(Guid Id, string Code, ProductionOrderStatus Status, DateTimeOffset? VoidedAt);
