using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Catalog.Commands.AddSecondaryUnit;

public sealed record AddSecondaryUnitCommand(
    Guid OrganizationId, Guid ProductId, Guid UnitId, decimal ConversionRate, decimal SellingPrice, decimal PurchasePrice)
    : IRequest<AddSecondaryUnitResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ProductManage;
}

public sealed record AddSecondaryUnitResult(Guid Id, Guid ProductId, Guid UnitId, decimal ConversionRate);
