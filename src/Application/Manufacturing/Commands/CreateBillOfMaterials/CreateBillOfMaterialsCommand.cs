using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.CreateBillOfMaterials;

public sealed record CreateBillOfMaterialsCommand(
    Guid OrganizationId,
    Guid ProductId,
    decimal OutputQuantity,
    bool ManufactureOnEverySale,
    string? Notes,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<CreateBillOfMaterialsResult>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.BillOfMaterialsManage;

    // Manufacturing is gated on two entitlements, not one: production is entirely a stock
    // operation, so a tenant that never opted into Track Inventory has no ledger for it to consume
    // from or create into (WarehouseTransfer set the precedent for a two-feature declaration).
    // Unlike MultipleWarehouses (phase-20f), a hard block is right here -- a Manufacturing-off
    // tenant loses nothing it could otherwise do, so there is no risk of wedging it.
    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record CreateBillOfMaterialsResult(Guid Id);
