using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateInvoice;

public sealed record CreateInvoiceCommand(
    Guid OrganizationId,
    Guid ContactId,
    Guid WarehouseId,
    DateOnly Date,
    string? Reference,
    IReadOnlyList<InvoiceLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0)
    : IRequest<CreateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.InvoiceCreate;
    public DocumentType AuditDocumentType => DocumentType.Invoice;
}

public sealed record CreateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
