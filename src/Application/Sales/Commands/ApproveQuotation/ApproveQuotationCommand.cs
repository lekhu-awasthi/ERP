using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveQuotation;

public sealed record ApproveQuotationCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.QuotationApprove;
    public DocumentType LockDateDocumentType => DocumentType.Quotation;
    public Guid LockDateDocumentId => Id;
}

public sealed record ApproveQuotationResult(Guid Id, string Code, QuotationStatus Status, DateTimeOffset? ApprovedAt);
