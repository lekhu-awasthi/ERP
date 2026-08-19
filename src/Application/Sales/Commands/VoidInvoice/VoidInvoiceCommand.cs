using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.VoidInvoice;

public sealed record VoidInvoiceCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.InvoiceVoid;
    public DocumentType LockDateDocumentType => DocumentType.Invoice;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidInvoiceResult(Guid Id, string Code, InvoiceStatus Status, DateTimeOffset? VoidedAt);
