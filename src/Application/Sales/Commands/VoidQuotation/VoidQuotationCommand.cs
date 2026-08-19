using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.VoidQuotation;

public sealed record VoidQuotationCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.QuotationVoid;
    public DocumentType LockDateDocumentType => DocumentType.Quotation;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidQuotationResult(Guid Id, string Code, QuotationStatus Status, DateTimeOffset? VoidedAt);
