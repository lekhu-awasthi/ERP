using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
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
    IReadOnlyList<PurchaseBillLineInput> Lines,
    decimal DiscountPct = 0)
    : IRequest<UpdatePurchaseBillResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.PurchaseBillEdit;
    public DocumentType AuditDocumentType => DocumentType.PurchaseBill;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePurchaseBillResult(Guid Id, string Code, PurchaseBillStatus Status);
