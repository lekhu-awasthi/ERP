using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.VoidExpense;

public sealed record VoidExpenseCommand(Guid OrganizationId, Guid Id)
    : IRequest<VoidExpenseResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitiveDocument
{
    public string PermissionKey => PermissionKeys.ExpenseVoid;
    public DocumentType LockDateDocumentType => DocumentType.Expense;
    public Guid LockDateDocumentId => Id;
}

public sealed record VoidExpenseResult(Guid Id, string Code, ExpenseStatus Status, DateTimeOffset? VoidedAt);
