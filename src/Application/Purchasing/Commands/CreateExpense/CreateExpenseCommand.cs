using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateExpense;

public sealed record CreateExpenseCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    DateOnly? DueDate,
    string? SupplierInvoiceReference,
    string? Notes,
    bool TdsApplicable,
    Guid? TdsTypeId,
    IReadOnlyList<ExpenseLineInput> Lines)
    : IRequest<CreateExpenseResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ExpenseCreate;
}

public sealed record CreateExpenseResult(Guid Id, string Code, ExpenseStatus Status);
