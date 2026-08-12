using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.ApproveQuotation;

public sealed record ApproveQuotationCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveQuotationResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.QuotationApprove;
}

public sealed record ApproveQuotationResult(Guid Id, string Code, QuotationStatus Status, DateTimeOffset? ApprovedAt);
