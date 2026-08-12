using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveInvoice;

public sealed record ApproveInvoiceCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveInvoiceResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.InvoiceApprove;
}

public sealed record ApproveInvoiceResult(Guid Id, string Code, InvoiceStatus Status, DateTimeOffset? ApprovedAt);
