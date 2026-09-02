using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateInvoice;

public sealed record UpdateInvoiceCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference,
    IReadOnlyList<InvoiceLineInput> Lines, decimal DiscountPct = 0,
    bool IsExport = false, string? ExportCountry = null, string? ExportDeclarationNo = null,
    DateOnly? ExportDeclarationDate = null)
    : IRequest<UpdateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId
{
    public string PermissionKey => PermissionKeys.InvoiceEdit;
    public DocumentType AuditDocumentType => DocumentType.Invoice;
    public Guid AuditDocumentId => Id;
}

public sealed record UpdateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
