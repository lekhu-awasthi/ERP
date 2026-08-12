using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApproveExpense;

public sealed record ApproveExpenseCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveExpenseResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExpenseApprove;
}

public sealed record ApproveExpenseResult(Guid Id, string Code, ExpenseStatus Status, DateTimeOffset? ApprovedAt);
