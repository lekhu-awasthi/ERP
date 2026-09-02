using ErpApp.Application.Common.Security;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Manufacturing.Commands.UpdateProductionJournal;

public sealed record UpdateProductionJournalCommand(
    Guid OrganizationId,
    Guid Id,
    DateOnly Date,
    string? Reference,
    Guid ProductId,
    decimal OutputQuantity,
    Guid WarehouseId,
    Guid? BillOfMaterialsId,
    string? Notes,
    IReadOnlyList<ProductionRawMaterialLineInput> RawMaterials,
    IReadOnlyList<ProductionByProductLineInput> ByProducts,
    IReadOnlyList<ProductionExpenseLineInput> Expenses)
    : IRequest<UpdateProductionJournalResult>, IRequirePermission, IOrganizationScoped, IRequireFeature, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ProductionJournalEdit;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures =>
        [TenantFeature.Manufacturing, TenantFeature.TrackInventory];
}

public sealed record UpdateProductionJournalResult(Guid Id, string Code, ProductionJournalStatus Status);
