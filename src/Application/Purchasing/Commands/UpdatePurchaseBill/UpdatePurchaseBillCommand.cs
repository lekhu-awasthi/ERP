using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;

public sealed record UpdatePurchaseBillCommand(
    Guid OrganizationId,
    Guid Id,
    Guid ContactId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    string? SupplierInvoiceReference,
    bool IsImport,
    string? ImportCountry,
    DateOnly? ImportDate,
    string? ImportDocumentNo,
    Guid? TdsTypeId,
    IReadOnlyList<PurchaseBillLineInput> Lines)
    : IRequest<UpdatePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.PurchaseBillEdit;
}

public sealed record UpdatePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status);
