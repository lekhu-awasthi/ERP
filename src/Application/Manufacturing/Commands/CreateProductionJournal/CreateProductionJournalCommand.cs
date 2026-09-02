using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.CreateProductionJournal;

/// <summary>
/// Raw-material lines carry a Quantity and no rate, deliberately: the cost is resolved at Approve
/// from the FIFO layers actually walked. See ProductionJournalRawMaterialLine.
/// </summary>
public sealed record CreateProductionJournalCommand(
    Guid OrganizationId,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid WarehouseId,
    Guid? BillOfMaterialsId,
    string? Notes,
    DocumentType? ReferrerType,
    Guid? ReferrerId,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<CreateProductionJournalResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ProductionJournalCreate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record CreateProductionJournalResult(Guid Id, string Code, ProductionJournalStatus Status);
