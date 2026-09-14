using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.ApproveExpense;

public sealed record ApproveExpenseCommand(Guid OrganizationId, Guid Id)
    : IRequest<ApproveExpenseResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument, IMeteredTransaction
{
    public string PermissionKey => PermissionKeys.ExpenseApprove;
    public DocumentType LockDateDocumentType => DocumentType.Expense;
    public Guid LockDateDocumentId => Id;

    public DocumentType MeteredDocumentType => DocumentType.Expense;
}

public sealed record ApproveExpenseResult(Guid Id, string Code, ExpenseStatus Status, DateTimeOffset? ApprovedAt);
